using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Channels;
using Lcia.Domain;
using Microsoft.Extensions.Logging;

namespace Lcia.Application;

/// <summary>
/// Caso d'uso "indicizza repository": scansione, confronto incrementale, analisi parallela,
/// scrittura in batch nell'indice, progetti e metadati Git.
/// Una scansione annullata lascia nell'indice i batch già salvati: la successiva riparte
/// saltando i file invariati (ripresa delle operazioni interrotte).
/// </summary>
public sealed class IndexingService
{
    public const int BatchSize = 100;
    private const int MaxStoredWarnings = 200;

    private readonly IRepositoryScanner _scanner;
    private readonly IWorkspaceDiscovery _workspace;
    private readonly ITextFileReader _reader;
    private readonly IReadOnlyList<ISourceAnalyzer> _analyzers;
    private readonly IIndexStore _store;
    private readonly IGitMetadataReader _git;
    private readonly ILogger<IndexingService> _logger;

    public IndexingService(
        IRepositoryScanner scanner,
        IWorkspaceDiscovery workspace,
        ITextFileReader reader,
        IEnumerable<ISourceAnalyzer> analyzers,
        IIndexStore store,
        IGitMetadataReader git,
        ILogger<IndexingService> logger)
    {
        _scanner = scanner;
        _workspace = workspace;
        _reader = reader;
        _analyzers = analyzers.ToArray();
        _store = store;
        _git = git;
        _logger = logger;
    }

    /// <summary>
    /// Interpreta il percorso scelto dall'utente: cartella oppure soluzione .sln/.slnx
    /// (in tal caso la radice è la cartella che contiene la soluzione).
    /// </summary>
    public static (string Root, string? EntryPath, string Name) ResolveTarget(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Percorso non specificato.", nameof(path));
        }

        var full = Path.GetFullPath(path.Trim().Trim('"'));
        if (File.Exists(full))
        {
            var extension = Path.GetExtension(full);
            if (!extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Il file selezionato non è una soluzione Visual Studio (.sln/.slnx): {full}", nameof(path));
            }

            var directory = Path.GetDirectoryName(full)
                ?? throw new ArgumentException($"Impossibile determinare la cartella della soluzione: {full}", nameof(path));
            return (PathUtilities.NormalizeRoot(directory), full, Path.GetFileNameWithoutExtension(full));
        }

        if (Directory.Exists(full))
        {
            var root = PathUtilities.NormalizeRoot(full);
            var name = Path.GetFileName(root);
            return (root, null, string.IsNullOrEmpty(name) ? root : name);
        }

        throw new DirectoryNotFoundException($"Percorso non trovato: {full}");
    }

    public async Task<IndexingResult> IndexAsync(
        IndexingRequest request,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (root, entryPath, name) = ResolveTarget(request.Path);
        var stopwatch = Stopwatch.StartNew();

        Report(progress, IndexingPhase.Preparing, 0, 0, null, "Preparazione dell'indice…");
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var repository = await _store.GetOrCreateRepositoryAsync(root, name, entryPath, cancellationToken).ConfigureAwait(false);
        var startedUtc = DateTimeOffset.UtcNow;
        var runId = await _store.BeginScanRunAsync(repository.Id, startedUtc, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Indicizzazione avviata: {Root} (repository {Id})", root, repository.Id);

        var counters = new Counters();
        var warnings = new WarningList();
        var status = ScanRunStatus.Running;
        string? message = null;

        try
        {
            var options = request.Options;

            Report(progress, IndexingPhase.Discovering, 0, 0, null, "Ricerca dei file…");
            var discovery = await _scanner.DiscoverAsync(root, options, cancellationToken).ConfigureAwait(false);
            warnings.AddRange(discovery.Warnings);
            counters.Seen = discovery.Files.Count;

            Report(progress, IndexingPhase.Comparing, 0, discovery.Files.Count, null, $"Confronto con l'indice ({discovery.Files.Count:N0} file)…");
            var fingerprints = await _store.GetFileFingerprintsAsync(repository.Id, cancellationToken).ConfigureAwait(false);

            var discoveredPaths = new HashSet<string>(discovery.Files.Select(f => f.RelativePath), StringComparer.OrdinalIgnoreCase);
            var removed = fingerprints.Keys.Where(k => !discoveredPaths.Contains(k)).ToList();
            if (removed.Count > 0)
            {
                await _store.RemoveFilesAsync(repository.Id, removed, cancellationToken).ConfigureAwait(false);
                counters.Removed = removed.Count;
            }

            var work = new List<(DiscoveredFile File, FileFingerprint? Previous)>();
            foreach (var file in discovery.Files)
            {
                fingerprints.TryGetValue(file.RelativePath, out var previous);
                var unchanged = !options.ForceReindex
                    && previous is not null
                    && previous.SizeBytes == file.SizeBytes
                    && previous.LastWriteTicks == file.LastWriteUtc.Ticks
                    && previous.IndexVersion == IndexFormat.Version;
                if (unchanged)
                {
                    counters.Unchanged++;
                }
                else
                {
                    work.Add((file, previous));
                }
            }

            await ProcessAsync(repository.Id, work, options, counters, warnings, progress, cancellationToken).ConfigureAwait(false);

            Report(progress, IndexingPhase.Projects, work.Count, work.Count, null, "Lettura di soluzioni e progetti…");
            var projectWarnings = new List<ScanWarning>();
            var projects = _workspace.Discover(root, discovery.Files, projectWarnings, cancellationToken);
            warnings.AddRange(projectWarnings);
            await _store.ReplaceProjectsAsync(repository.Id, projects, cancellationToken).ConfigureAwait(false);

            var git = _git.TryRead(root);
            await _store.UpdateRepositoryScanInfoAsync(repository.Id, DateTimeOffset.UtcNow, entryPath, git, cancellationToken).ConfigureAwait(false);

            status = ScanRunStatus.Completed;
            message = $"{counters.Indexed} file analizzati, {counters.Unchanged} invariati, {counters.Removed} rimossi, {counters.Skipped} saltati, {counters.Errors} errori.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = ScanRunStatus.Cancelled;
            message = "Annullata dall'utente: i file già elaborati restano nell'indice e verranno saltati alla prossima esecuzione.";
            _logger.LogWarning("Indicizzazione annullata: {Root}", root);
        }
        catch (Exception ex)
        {
            status = ScanRunStatus.Failed;
            message = ex.Message;
            _logger.LogError(ex, "Indicizzazione non riuscita: {Root}", root);
        }

        var run = new ScanRun
        {
            Id = runId,
            RepositoryId = repository.Id,
            StartedUtc = startedUtc,
            FinishedUtc = DateTimeOffset.UtcNow,
            Status = status,
            FilesSeen = counters.Seen,
            FilesIndexed = counters.Indexed,
            FilesUnchanged = counters.Unchanged,
            FilesRemoved = counters.Removed,
            FilesSkipped = counters.Skipped,
            Errors = counters.Errors,
            WarningCount = warnings.Count,
            WarningsText = warnings.ToText(MaxStoredWarnings),
            Message = message,
        };

        try
        {
            await _store.CompleteScanRunAsync(run, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Impossibile registrare l'esito della scansione {RunId}", runId);
        }

        var refreshed = await _store.GetRepositoryAsync(repository.Id, CancellationToken.None).ConfigureAwait(false) ?? repository;
        Report(progress, IndexingPhase.Completed, counters.Indexed, counters.Indexed, null, message ?? string.Empty);
        _logger.LogInformation("Indicizzazione terminata ({Status}) in {Elapsed}: {Message}", status, stopwatch.Elapsed, message);
        return new IndexingResult(refreshed, run, warnings.Snapshot(), stopwatch.Elapsed);
    }

    private async Task ProcessAsync(
        long repositoryId,
        List<(DiscoveredFile File, FileFingerprint? Previous)> work,
        ScanOptions options,
        Counters counters,
        WarningList warnings,
        IProgress<IndexingProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (work.Count == 0)
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = Channel.CreateBounded<FileWorkItem>(new BoundedChannelOptions(BatchSize * 2)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

        // Unico consumatore: SQLite accetta un solo scrittore. Il consumatore svuota sempre il canale,
        // anche dopo un annullamento, così i file già elaborati vengono salvati.
        var consumer = Task.Run(async () =>
        {
            try
            {
                await ConsumeAsync(repositoryId, channel.Reader, work.Count, counters, warnings, progress).ConfigureAwait(false);
            }
            catch
            {
                linked.Cancel();
                throw;
            }
        }, CancellationToken.None);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(options.MaxDegreeOfParallelism, 1, 64),
            CancellationToken = linked.Token,
        };

        Exception? producerError = null;
        try
        {
            await Parallel.ForEachAsync(work, parallelOptions, async (entry, token) =>
            {
                var item = BuildWorkItem(repositoryId, entry.File, entry.Previous, options, token);
                await channel.Writer.WriteAsync(item, token).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            producerError = ex;
        }
        finally
        {
            channel.Writer.TryComplete();
        }

        await consumer.ConfigureAwait(false);
        if (producerError is not null)
        {
            ExceptionDispatchInfo.Capture(producerError).Throw();
        }
    }

    private async Task ConsumeAsync(
        long repositoryId,
        ChannelReader<FileWorkItem> reader,
        int total,
        Counters counters,
        WarningList warnings,
        IProgress<IndexingProgress>? progress)
    {
        var batch = new List<FileWorkItem>(BatchSize);
        var processed = 0;
        var sinceFlush = Stopwatch.StartNew();

        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out var item))
            {
                batch.Add(item);
                if (batch.Count >= BatchSize)
                {
                    await FlushAsync().ConfigureAwait(false);
                }
            }

            if (batch.Count > 0 && sinceFlush.ElapsedMilliseconds > 1500)
            {
                await FlushAsync().ConfigureAwait(false);
            }
        }

        await FlushAsync().ConfigureAwait(false);

        async Task FlushAsync()
        {
            if (batch.Count == 0)
            {
                return;
            }

            var items = batch.ToArray();
            batch.Clear();
            await _store.SaveFilesAsync(repositoryId, items, CancellationToken.None).ConfigureAwait(false);
            foreach (var item in items)
            {
                Count(item, counters, warnings);
            }

            processed += items.Length;
            sinceFlush.Restart();
            Report(progress, IndexingPhase.Analyzing, processed, total, items[^1].File.RelativePath,
                $"Analisi e indicizzazione: {processed:N0} di {total:N0} file");
        }
    }

    private static void Count(FileWorkItem item, Counters counters, WarningList warnings)
    {
        if (item.Kind == FileWorkKind.MetadataOnly)
        {
            counters.Unchanged++;
            return;
        }

        switch (item.File.Status)
        {
            case FileIndexStatus.Indexed:
                counters.Indexed++;
                if (!string.IsNullOrEmpty(item.File.StatusDetail))
                {
                    warnings.Add(new ScanWarning(item.File.RelativePath, item.File.StatusDetail));
                }

                break;
            case FileIndexStatus.Unreadable:
                counters.Errors++;
                warnings.Add(new ScanWarning(item.File.RelativePath, item.File.StatusDetail ?? "File non leggibile."));
                break;
            default:
                counters.Skipped++;
                if (item.File.Status == FileIndexStatus.TooLarge)
                {
                    warnings.Add(new ScanWarning(item.File.RelativePath, item.File.StatusDetail ?? "File troppo grande."));
                }

                break;
        }
    }

    private FileWorkItem BuildWorkItem(long repositoryId, DiscoveredFile file, FileFingerprint? previous, ScanOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = new IndexedFile
        {
            RepositoryId = repositoryId,
            RelativePath = file.RelativePath,
            Extension = file.Extension.ToLowerInvariant(),
            Language = file.Language,
            SizeBytes = file.SizeBytes,
            LastWriteUtc = file.LastWriteUtc,
            Status = FileIndexStatus.Indexed,
            AnalysisLevel = SupportLevel.TextOnly,
            IndexVersion = IndexFormat.Version,
            IndexedUtc = DateTimeOffset.UtcNow,
        };

        if (file.SizeBytes > options.MaxFileSizeBytes)
        {
            return new FileWorkItem(FileWorkKind.Full, record with
            {
                Status = FileIndexStatus.TooLarge,
                StatusDetail = $"Dimensione {file.SizeBytes:N0} byte oltre il limite di {options.MaxFileSizeBytes:N0} byte: elencato ma non indicizzato.",
            }, null, null);
        }

        var read = _reader.Read(file.FullPath, options.MaxFileSizeBytes, cancellationToken);
        switch (read.Status)
        {
            case TextReadStatus.TooLarge:
                return new FileWorkItem(FileWorkKind.Full, record with
                {
                    Status = FileIndexStatus.TooLarge,
                    SizeBytes = read.SizeBytes,
                    StatusDetail = read.Error ?? "File oltre il limite di dimensione.",
                }, null, null);
            case TextReadStatus.Unreadable:
                return new FileWorkItem(FileWorkKind.Full, record with
                {
                    Status = FileIndexStatus.Unreadable,
                    StatusDetail = read.Error ?? "Lettura non riuscita.",
                }, null, null);
            case TextReadStatus.Binary:
                return new FileWorkItem(FileWorkKind.Full, record with
                {
                    Status = FileIndexStatus.Binary,
                    Sha256 = read.Sha256,
                    StatusDetail = "Contenuto binario: escluso da indicizzazione ed esportazione.",
                }, null, null);
        }

        if (!options.ForceReindex
            && previous is not null
            && previous.Status == FileIndexStatus.Indexed
            && previous.IndexVersion == IndexFormat.Version
            && string.Equals(previous.Sha256, read.Sha256, StringComparison.Ordinal))
        {
            return new FileWorkItem(FileWorkKind.MetadataOnly, record with { Id = previous.FileId, Sha256 = read.Sha256 }, null, null);
        }

        var text = read.Text ?? string.Empty;
        var analysis = SourceAnalysisResult.Empty;
        string? detail = null;
        var analyzer = _analyzers.FirstOrDefault(a => a.CanAnalyze(file.Language));
        if (analyzer is not null)
        {
            try
            {
                analysis = analyzer.Analyze(file.RelativePath, text, cancellationToken);
                if (analysis.Diagnostics.Count > 0)
                {
                    detail = string.Join(" | ", analysis.Diagnostics);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                detail = $"Analisi {analyzer.Name} non riuscita: {ex.Message}. Il file resta disponibile per la ricerca testuale.";
                analysis = SourceAnalysisResult.Empty;
                _logger.LogWarning(ex, "Analisi non riuscita per {Path}", file.RelativePath);
            }
        }

        var analyzed = record with
        {
            Sha256 = read.Sha256,
            LineCount = read.LineCount,
            Encoding = read.EncodingName,
            StatusDetail = detail,
            AnalysisLevel = analysis.Level,
        };
        return new FileWorkItem(FileWorkKind.Full, analyzed, text, analysis);
    }

    private static void Report(IProgress<IndexingProgress>? progress, IndexingPhase phase, int processed, int total, string? path, string message) =>
        progress?.Report(new IndexingProgress(phase, processed, total, path, message));

    private sealed class Counters
    {
        public int Seen;
        public int Indexed;
        public int Unchanged;
        public int Removed;
        public int Skipped;
        public int Errors;
    }

    /// <summary>Lista di avvisi thread-safe.</summary>
    private sealed class WarningList
    {
        private readonly List<ScanWarning> _items = [];
        private readonly Lock _lock = new();

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _items.Count;
                }
            }
        }

        public void Add(ScanWarning warning)
        {
            lock (_lock)
            {
                _items.Add(warning);
            }
        }

        public void AddRange(IEnumerable<ScanWarning> warnings)
        {
            lock (_lock)
            {
                _items.AddRange(warnings);
            }
        }

        public IReadOnlyList<ScanWarning> Snapshot()
        {
            lock (_lock)
            {
                return _items.ToArray();
            }
        }

        public string? ToText(int max)
        {
            lock (_lock)
            {
                if (_items.Count == 0)
                {
                    return null;
                }

                var builder = new StringBuilder();
                foreach (var warning in _items.Take(max))
                {
                    builder.AppendLine(warning.ToString());
                }

                if (_items.Count > max)
                {
                    builder.AppendLine($"… altri {_items.Count - max} avvisi non memorizzati.");
                }

                return builder.ToString().TrimEnd();
            }
        }
    }
}
