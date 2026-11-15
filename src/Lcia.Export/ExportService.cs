using Lcia.Application;
using Lcia.Domain;
using Microsoft.Extensions.Logging;

namespace Lcia.Export;

/// <summary>
/// FULL PROJECT CONTEXT EXPORT. Legge i file in sola lettura dal disco (contenuto corrente),
/// oscura i segreti, scrive il dossier in una cartella esterna al repository e, se suddiviso,
/// un file indice che descrive parti e ordine di lettura. Non modifica mai i file originali.
/// </summary>
public sealed class ExportService : IExportService
{
    private const long MaxExportFileBytes = 64L * 1024 * 1024;
    private const int EstimatedOverheadPerFile = 900;
    private const int EstimatedHeaderCharacters = 4000;

    private readonly IIndexStore _store;
    private readonly ITextFileReader _reader;
    private readonly ISecretScanner _secrets;
    private readonly ILogger<ExportService> _logger;

    public ExportService(IIndexStore store, ITextFileReader reader, ISecretScanner secrets, ILogger<ExportService> logger)
    {
        _store = store;
        _reader = reader;
        _secrets = secrets;
        _logger = logger;
    }

    public static string ToolVersion => typeof(ExportService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public async Task<ExportPlan> PlanAsync(ExportRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var repository = await GetRepositoryAsync(request.RepositoryId, cancellationToken).ConfigureAwait(false);
        var selection = await SelectAsync(request, repository, cancellationToken).ConfigureAwait(false);

        long characters = EstimatedHeaderCharacters;
        foreach (var file in selection.Files)
        {
            characters += EstimatedOverheadPerFile + file.RelativePath.Length * 3;
            if (request.IncludeSourceCode)
            {
                characters += file.SizeBytes;
                if (request.IncludeLineNumbers)
                {
                    characters += file.LineCount * (long)(Math.Max(3, file.LineCount.ToString().Length) + 3);
                }
            }
        }

        var maxCharacters = TokenEstimator.ToCharacters(request.MaxPartTokens);
        var parts = maxCharacters > 0 ? (int)Math.Max(1, (characters + maxCharacters - 1) / maxCharacters) : 1;
        return new ExportPlan(
            selection.Files.Select(f => new ExportFileEntry(f.RelativePath, f.Language, f.SizeBytes, f.LineCount)).ToArray(),
            characters,
            TokenEstimator.Estimate(characters),
            parts,
            selection.Warnings,
            selection.Sensitive);
    }

    public async Task<ExportResult> ExportAsync(ExportRequest request, IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        Validate(request);
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var repository = await GetRepositoryAsync(request.RepositoryId, cancellationToken).ConfigureAwait(false);

        var outputDirectory = Path.GetFullPath(request.OutputDirectory);
        if (PathUtilities.IsSameOrSubPath(repository.RootPath, outputDirectory))
        {
            throw new InvalidOperationException(
                "La cartella di destinazione si trova all'interno del repository analizzato. " +
                "Per non alterare il repository scegli una cartella esterna.");
        }

        Directory.CreateDirectory(outputDirectory);
        var selection = await SelectAsync(request, repository, cancellationToken).ConfigureAwait(false);
        if (selection.Files.Count == 0)
        {
            throw new InvalidOperationException("Nessun file corrisponde ai criteri di esportazione (filtri, selezione o file non indicizzati).");
        }

        var projects = await _store.ListProjectsAsync(repository.Id, cancellationToken).ConfigureAwait(false);
        var projectNames = projects.Where(p => p.Kind == ProjectKind.Project).ToDictionary(p => p.Id, p => p.Name);
        string? ProjectName(long? id) => id is long value && projectNames.TryGetValue(value, out var name) ? name : null;

        var symbols = new Dictionary<long, IReadOnlyList<CodeSymbol>>();
        var relations = new Dictionary<long, IReadOnlyList<SymbolRelation>>();
        foreach (var file in selection.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            symbols[file.Id] = await _store.ListSymbolsAsync(file.Id, cancellationToken).ConfigureAwait(false);
            relations[file.Id] = await _store.ListRelationsAsync(file.Id, cancellationToken).ConfigureAwait(false);
        }

        var formatter = ExportFormatter.Create(request.Format);
        var generatedUtc = DateTimeOffset.UtcNow;
        var baseName = PathUtilities.SanitizeFileName(string.IsNullOrWhiteSpace(request.BaseFileName)
            ? $"{repository.Name}-{(request.Scope == ExportScope.Full ? "full" : "custom")}-{DateTime.Now:yyyyMMdd-HHmmss}"
            : request.BaseFileName);
        var estimatedCharacters = EstimatedHeaderCharacters + selection.Files.Sum(f => f.SizeBytes + EstimatedOverheadPerFile);
        var header = new ExportHeaderModel(
            repository,
            request,
            selection.Files.Count,
            selection.Files.Sum(f => (long)f.LineCount),
            selection.Files.Sum(f => f.SizeBytes),
            TokenEstimator.Estimate(estimatedCharacters),
            ToolVersion,
            generatedUtc,
            baseName);

        var warnings = new List<string>(selection.Warnings);
        var redactions = new List<ExportRedaction>();
        var modified = new List<string>();
        long totalCharacters = 0;

        var writer = new ExportPartWriter(outputDirectory, baseName, TokenEstimator.ToCharacters(request.MaxPartTokens), formatter);
        try
        {
            async Task WriteAsync(string text, string? label)
            {
                totalCharacters += text.Length;
                await writer.WriteAsync(text, label, cancellationToken).ConfigureAwait(false);
            }

            await WriteAsync(formatter.DocumentHeader(header), null).ConfigureAwait(false);
            if (request.IncludeDirectoryTree)
            {
                await WriteAsync(formatter.DirectoryTree(selection.Files.Select(f => f.RelativePath).ToArray()), null).ConfigureAwait(false);
            }

            if (request.IncludeFileList)
            {
                await WriteAsync(formatter.FileList(selection.Files, ProjectName), null).ConfigureAwait(false);
            }

            if (request.IncludeProjects)
            {
                await WriteAsync(formatter.Projects(projects), null).ConfigureAwait(false);
            }

            if (request.IncludeSymbolIndex)
            {
                await WriteAsync(formatter.SymbolIndex(selection.Files, symbols), null).ConfigureAwait(false);
                await WriteAsync(formatter.CrossReferences(selection.Files, relations), null).ConfigureAwait(false);
            }

            await WriteAsync(formatter.SourceFilesTitle(), null).ConfigureAwait(false);

            for (var i = 0; i < selection.Files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = selection.Files[i];
                progress?.Report(new ExportProgress(i, selection.Files.Count, file.RelativePath));

                IReadOnlyList<string>? lines = null;
                string? unavailable = null;
                IReadOnlyList<SecretFinding> findings = [];
                var isModified = false;

                if (request.IncludeSourceCode)
                {
                    var fullPath = PathUtilities.ResolveInside(repository.RootPath, file.RelativePath);
                    var read = _reader.Read(fullPath, MaxExportFileBytes, cancellationToken);
                    if (read.Status == TextReadStatus.Ok && read.Text is not null)
                    {
                        isModified = !string.Equals(read.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase);
                        var text = read.Text;
                        if (request.RedactSecrets)
                        {
                            var redaction = _secrets.Redact(text, file.Language, request.Redaction);
                            text = redaction.Text;
                            findings = redaction.Findings;
                        }

                        lines = TextLines.Split(text);
                    }
                    else
                    {
                        unavailable = read.Status switch
                        {
                            TextReadStatus.Binary => "the file is now binary",
                            TextReadStatus.TooLarge => "the file is now larger than the export limit",
                            _ => "the file could not be read: " + (read.Error ?? "unknown error"),
                        };
                        warnings.Add($"{file.RelativePath}: contenuto non esportato ({unavailable}).");
                    }
                }

                if (isModified)
                {
                    modified.Add(file.RelativePath);
                }

                redactions.AddRange(findings.Select(f => new ExportRedaction(file.RelativePath, f.Line, f.Category)));
                var block = formatter.FileBlock(new FileBlockModel(
                    file,
                    ProjectName(file.ProjectId),
                    symbols[file.Id],
                    relations[file.Id],
                    lines,
                    unavailable,
                    findings,
                    isModified,
                    request.IncludeSourceCode,
                    request.IncludeLineNumbers));
                await WriteAsync(block, file.RelativePath).ConfigureAwait(false);
            }

            progress?.Report(new ExportProgress(selection.Files.Count, selection.Files.Count, null));
            await WriteAsync(formatter.Summary(new ExportSummaryModel(
                selection.Files.Count,
                totalCharacters,
                TokenEstimator.Estimate(totalCharacters),
                Math.Max(1, writer.PartCount),
                redactions,
                modified,
                warnings,
                selection.Sensitive,
                request.RedactSecrets)), null).ConfigureAwait(false);

            var parts = await writer.CompleteAsync().ConfigureAwait(false);
            string? indexPath = null;
            if (parts.Count > 1)
            {
                indexPath = Path.Combine(outputDirectory, writer.IndexFileName);
                var index = formatter.IndexDocument(new ExportIndexModel(header, parts, selection.Files.Select(f => f.RelativePath).ToArray()));
                await using var stream = new FileStream(indexPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                await using var indexWriter = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
                await indexWriter.WriteAsync(index.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Esportazione completata: {Files} file, {Parts} parti, ~{Tokens} token, {Redactions} valori oscurati, destinazione {Directory}",
                selection.Files.Count, parts.Count, TokenEstimator.Estimate(totalCharacters), redactions.Count, outputDirectory);

            return new ExportResult(
                parts,
                indexPath,
                selection.Files.Count,
                totalCharacters,
                TokenEstimator.Estimate(totalCharacters),
                redactions,
                modified,
                warnings,
                selection.Sensitive);
        }
        catch
        {
            await writer.AbortAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            await writer.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void Validate(ExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            throw new ArgumentException("Cartella di destinazione non specificata.", nameof(request));
        }

        if (!request.RedactSecrets && !request.UnredactedExportConfirmed)
        {
            throw new InvalidOperationException(
                "Per esportare senza oscurare i segreti è necessaria una conferma esplicita e consapevole.");
        }

        if (request.IncludeSensitiveFiles && !request.SensitiveFilesConfirmed)
        {
            throw new InvalidOperationException(
                "Per includere file sensibili (secrets.json, .env, chiavi) è necessaria una conferma esplicita.");
        }

        if (request.MaxPartTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "La dimensione massima per parte non può essere negativa.");
        }
    }

    private async Task<RepositoryInfo> GetRepositoryAsync(long repositoryId, CancellationToken cancellationToken) =>
        await _store.GetRepositoryAsync(repositoryId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException("Repository non presente nell'indice: indicizzalo prima di esportare.");

    private async Task<Selection> SelectAsync(ExportRequest request, RepositoryInfo repository, CancellationToken cancellationToken)
    {
        var all = await _store.ListFilesAsync(repository.Id, cancellationToken).ConfigureAwait(false);
        var warnings = new List<string>();
        var sensitive = new List<string>();
        IEnumerable<IndexedFile> candidates = all;

        if (request.Scope == ExportScope.Custom)
        {
            var selected = new HashSet<string>(request.SelectedFiles.Select(PathUtilities.NormalizeRelative).Where(p => p.Length > 0), StringComparer.OrdinalIgnoreCase);
            if (selected.Count == 0)
            {
                warnings.Add("Esportazione personalizzata senza file selezionati.");
            }

            var known = new HashSet<string>(all.Select(f => f.RelativePath), StringComparer.OrdinalIgnoreCase);
            warnings.AddRange(selected.Where(s => !known.Contains(s)).Select(s => $"File selezionato non presente nell'indice: {s}"));
            candidates = candidates.Where(f => selected.Contains(f.RelativePath));
        }

        var includePrefixes = request.IncludePathPrefixes.Select(PathUtilities.NormalizeRelative).Where(p => p.Length > 0).ToArray();
        var excludePrefixes = request.ExcludePathPrefixes.Select(PathUtilities.NormalizeRelative).Where(p => p.Length > 0).ToArray();
        var extensions = new HashSet<string>(
            request.IncludeExtensions.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim().StartsWith('.') ? e.Trim() : "." + e.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var files = new List<IndexedFile>();
        var notIndexed = 0;
        foreach (var file in candidates)
        {
            if (includePrefixes.Length > 0 && !includePrefixes.Any(p => PathUtilities.HasRelativePrefix(file.RelativePath, p)))
            {
                continue;
            }

            if (excludePrefixes.Any(p => PathUtilities.HasRelativePrefix(file.RelativePath, p)))
            {
                continue;
            }

            if (extensions.Count > 0 && !extensions.Contains(file.Extension))
            {
                continue;
            }

            if (file.Status != FileIndexStatus.Indexed)
            {
                notIndexed++;
                continue;
            }

            if (SensitiveFiles.IsSensitive(file.RelativePath) && !(request.IncludeSensitiveFiles && request.SensitiveFilesConfirmed))
            {
                sensitive.Add(file.RelativePath);
                continue;
            }

            files.Add(file);
        }

        if (notIndexed > 0)
        {
            warnings.Add($"{notIndexed} file esclusi perché binari, oltre il limite di dimensione o non leggibili durante l'indicizzazione.");
        }

        if (sensitive.Count > 0)
        {
            warnings.Add($"{sensitive.Count} file sensibili esclusi per sicurezza (es. secrets.json, .env).");
        }

        files.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
        return new Selection(files, warnings, sensitive);
    }

    private sealed record Selection(IReadOnlyList<IndexedFile> Files, IReadOnlyList<string> Warnings, IReadOnlyList<string> Sensitive);
}
