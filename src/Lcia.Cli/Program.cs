using System.Globalization;
using System.Text;
using Lcia.Application;
using Lcia.Domain;
using Lcia.Export;
using Lcia.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lcia.Cli;

/// <summary>
/// Riga di comando di Local Code Intelligence Analyzer. Non modifica mai i repository analizzati.
/// Codici di uscita: 0 = ok, 1 = errore, 2 = uso non corretto, 3 = annullato.
/// </summary>
public static class Program
{
    private const string Usage = """
        Local Code Intelligence Analyzer - riga di comando

        Uso:
          lcia index  <cartella|soluzione.sln|soluzione.slnx> [--force] [--max-file-kb N] [--parallel N] [--follow-links]
          lcia search <cartella> <testo> [--mode words|exact|regex|symbol|refs] [--ext .cs,.sql] [--path testo]
                      [--project nome] [--case] [--limit N] [--show-secrets]
          lcia export <cartella> --out <cartella-destinazione> [--format txt|md] [--ext .cs,.sql] [--folder src/A,src/B]
                      [--exclude-folder test] [--files a.cs,b.cs] [--max-tokens N] [--name nome] [--no-line-numbers]
                      [--no-source] [--no-tree] [--no-symbols] [--absolute-paths]
                      [--mask-personal-data] [--password-only]
                      [--no-redact --i-understand-secrets-risk]
          lcia stats  <cartella>
          lcia repos
          lcia remove <cartella> --yes
          lcia --version | --help

        Opzioni comuni:
          --data-dir <cartella>   cartella dati (indice, log, impostazioni). Predefinita: %LOCALAPPDATA%\LocalCodeIntelligenceAnalyzer
          --verbose               log dettagliati sulla console

        Dati sensibili: password, token, chiavi, certificati e connection string vengono oscurati nei risultati
        di search e nei dossier di export, secondo le impostazioni dell'app (connection string complete, email e IP).
          --show-secrets          (search) mostra le anteprime senza oscuramento, solo sul tuo terminale
          --mask-personal-data    (export) oscura anche email e indirizzi IP
          --password-only         (export) delle connection string oscura solo la password

        Il comando export richiede che il repository sia già indicizzato (lcia index).
        I file prodotti vengono scritti solo nella cartella indicata con --out, che deve essere esterna al repository.
        """;

    public static async Task<int> Main(string[] args)
    {
        TrySetUtf8Console();
        if (args.Length == 0 || args[0] is "--help" or "-h" or "/?")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        if (args[0] is "--version" or "-v")
        {
            Console.WriteLine($"lcia {ExportService.ToolVersion}");
            return 0;
        }

        CommandLine command;
        try
        {
            command = CommandLine.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine("Errore: " + ex.Message);
            Console.Error.WriteLine("Usa 'lcia --help' per l'elenco dei comandi.");
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
            Console.Error.WriteLine("Annullamento richiesto…");
        };

        var services = new ServiceCollection();
        services.AddLocalCodeIntelligence(options =>
        {
            options.DataDirectory = command.Option("data-dir");
            options.MinimumLogLevel = command.Flag("verbose") ? LogLevel.Debug : LogLevel.Information;
        });
        await using var provider = services.BuildServiceProvider();

        try
        {
            return command.Name switch
            {
                "index" => await IndexAsync(provider, command, cancellation.Token),
                "search" => await SearchAsync(provider, command, cancellation.Token),
                "export" => await ExportAsync(provider, command, cancellation.Token),
                "stats" => await StatsAsync(provider, command, cancellation.Token),
                "repos" => await ReposAsync(provider, cancellation.Token),
                "remove" => await RemoveAsync(provider, command, cancellation.Token),
                _ => Fail($"Comando sconosciuto: {command.Name}", 2),
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operazione annullata.");
            return 3;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Fail(ex.Message, 1);
        }
    }

    private static async Task<int> IndexAsync(IServiceProvider provider, CommandLine command, CancellationToken cancellationToken)
    {
        var path = command.Argument(0, "percorso da indicizzare");
        var settings = provider.GetRequiredService<ISettingsStore>().Load();
        var options = settings.ToScanOptions() with
        {
            ForceReindex = command.Flag("force"),
            FollowSymbolicLinks = command.Flag("follow-links") || settings.FollowSymbolicLinks,
        };

        if (command.Option("max-file-kb") is { } maxKb)
        {
            options = options with { MaxFileSizeBytes = ParseInt(maxKb, "max-file-kb") * 1024L };
        }

        if (command.Option("parallel") is { } parallel)
        {
            options = options with { MaxDegreeOfParallelism = ParseInt(parallel, "parallel") };
        }

        var indexing = provider.GetRequiredService<IndexingService>();
        var lastPhase = (IndexingPhase)(-1);
        var progress = new SynchronousProgress<IndexingProgress>(p =>
        {
            if (p.Phase != lastPhase || p.Phase == IndexingPhase.Analyzing)
            {
                Console.WriteLine(p.Phase == IndexingPhase.Analyzing ? $"  {p.Message}" : p.Message);
                lastPhase = p.Phase;
            }
        });

        var result = await indexing.IndexAsync(new IndexingRequest(path, options), progress, cancellationToken);
        Console.WriteLine();
        Console.WriteLine($"Repository : {result.Repository.Name} (id {result.Repository.Id})");
        Console.WriteLine($"Radice     : {result.Repository.RootPath}");
        Console.WriteLine($"Esito      : {result.Run.Status} in {result.Elapsed.TotalSeconds:N1} s");
        Console.WriteLine($"File       : {result.Run.FilesSeen} trovati, {result.Run.FilesIndexed} analizzati, {result.Run.FilesUnchanged} invariati, " +
                          $"{result.Run.FilesRemoved} rimossi, {result.Run.FilesSkipped} saltati, {result.Run.Errors} errori");
        if (result.Warnings.Count > 0)
        {
            Console.WriteLine($"Avvisi     : {result.Warnings.Count}");
            foreach (var warning in result.Warnings.Take(20))
            {
                Console.WriteLine("  - " + warning);
            }

            if (result.Warnings.Count > 20)
            {
                Console.WriteLine($"  … altri {result.Warnings.Count - 20}");
            }
        }

        return result.Run.Status switch
        {
            ScanRunStatus.Completed => 0,
            ScanRunStatus.Cancelled => 3,
            _ => Fail(result.Run.Message ?? "Indicizzazione non riuscita.", 1),
        };
    }

    private static async Task<int> SearchAsync(IServiceProvider provider, CommandLine command, CancellationToken cancellationToken)
    {
        var repository = await RequireRepositoryAsync(provider, command.Argument(0, "cartella del repository"), cancellationToken);
        var text = command.Argument(1, "testo da cercare");
        var mode = command.Option("mode")?.ToLowerInvariant() switch
        {
            null or "words" => SearchMode.Words,
            "exact" => SearchMode.ExactText,
            "regex" => SearchMode.Regex,
            "symbol" => SearchMode.Symbol,
            "refs" or "references" => SearchMode.References,
            var other => throw new ArgumentException($"Modalità non valida: {other}"),
        };

        var settings = provider.GetRequiredService<ISettingsStore>().Load();
        var showSecrets = command.Flag("show-secrets");
        var query = new SearchQuery
        {
            MaskSensitive = settings.MaskSensitiveWhileBrowsing && !showSecrets,
            Redaction = settings.ToRedactionOptions(),
            RepositoryId = repository.Id,
            Text = text,
            Mode = mode,
            CaseSensitive = command.Flag("case"),
            Extensions = command.List("ext"),
            PathContains = command.Option("path"),
            ProjectName = command.Option("project"),
            MaxResults = command.Option("limit") is { } limit ? ParseInt(limit, "limit") : 100,
        };

        var result = await provider.GetRequiredService<ISearchService>().SearchAsync(query, cancellationToken);
        foreach (var hit in result.Hits)
        {
            var detail = string.IsNullOrEmpty(hit.Detail) ? string.Empty : $"  [{hit.Detail}]";
            Console.WriteLine($"{hit.RelativePath}:{hit.Line}: {hit.Preview}{detail}");
        }

        Console.WriteLine();
        Console.WriteLine($"{result.Hits.Count} risultati in {result.Elapsed.TotalMilliseconds:N0} ms{(result.Truncated ? " (troncati: aumenta --limit)" : string.Empty)}");
        Console.WriteLine(query.MaskSensitive
            ? "Valori sensibili oscurati nelle anteprime (--show-secrets per vederli solo in locale)."
            : "ATTENZIONE: anteprime senza oscuramento: non copiarle in servizi online.");
        if (!string.IsNullOrEmpty(result.Warning))
        {
            Console.WriteLine("Avviso: " + result.Warning);
        }

        return 0;
    }

    private static async Task<int> ExportAsync(IServiceProvider provider, CommandLine command, CancellationToken cancellationToken)
    {
        var repository = await RequireRepositoryAsync(provider, command.Argument(0, "cartella del repository"), cancellationToken);
        var output = command.Option("out") ?? throw new ArgumentException("Specifica la cartella di destinazione con --out.");
        var noRedact = command.Flag("no-redact");
        if (noRedact && !command.Flag("i-understand-secrets-risk"))
        {
            throw new ArgumentException("--no-redact richiede anche --i-understand-secrets-risk: l'esportazione potrebbe contenere credenziali.");
        }

        var files = command.List("files");
        var settings = provider.GetRequiredService<ISettingsStore>().Load();
        var redaction = settings.ToRedactionOptions() with
        {
            FullConnectionStrings = settings.MaskFullConnectionStrings && !command.Flag("password-only"),
            PersonalData = settings.MaskPersonalData || command.Flag("mask-personal-data"),
        };
        var request = new ExportRequest
        {
            Redaction = redaction,
            RepositoryId = repository.Id,
            OutputDirectory = output,
            Format = command.Option("format")?.ToLowerInvariant() is "md" or "markdown" ? ExportFormat.Markdown : ExportFormat.Text,
            Scope = files.Count > 0 ? ExportScope.Custom : ExportScope.Full,
            SelectedFiles = files,
            IncludeExtensions = command.List("ext"),
            IncludePathPrefixes = command.List("folder"),
            ExcludePathPrefixes = command.List("exclude-folder"),
            IncludeLineNumbers = !command.Flag("no-line-numbers"),
            IncludeSourceCode = !command.Flag("no-source"),
            IncludeDirectoryTree = !command.Flag("no-tree"),
            IncludeSymbolIndex = !command.Flag("no-symbols"),
            IncludeAbsolutePaths = command.Flag("absolute-paths"),
            RedactSecrets = !noRedact,
            UnredactedExportConfirmed = noRedact,
            MaxPartTokens = command.Option("max-tokens") is { } tokens ? ParseInt(tokens, "max-tokens") : 0,
            BaseFileName = command.Option("name"),
        };

        var export = provider.GetRequiredService<IExportService>();
        var plan = await export.PlanAsync(request, cancellationToken);
        Console.WriteLine($"Stima: {plan.Files.Count} file, ~{plan.EstimatedTokens:N0} token, {plan.EstimatedParts} parte/i.");

        var result = await export.ExportAsync(request, null, cancellationToken);
        Console.WriteLine($"Esportati {result.FilesExported} file, {result.TotalCharacters:N0} caratteri (~{result.EstimatedTokens:N0} token).");
        foreach (var path in result.WrittenFiles)
        {
            Console.WriteLine("  " + path);
        }

        Console.WriteLine(request.RedactSecrets
            ? $"Valori oscurati: {result.Redactions.Count}"
            : "ATTENZIONE: oscuramento dei segreti disattivato su richiesta esplicita.");
        foreach (var warning in result.Warnings)
        {
            Console.WriteLine("Avviso: " + warning);
        }

        return 0;
    }

    private static async Task<int> StatsAsync(IServiceProvider provider, CommandLine command, CancellationToken cancellationToken)
    {
        var repository = await RequireRepositoryAsync(provider, command.Argument(0, "cartella del repository"), cancellationToken);
        var store = provider.GetRequiredService<IIndexStore>();
        var stats = await store.GetStatisticsAsync(repository.Id, cancellationToken);
        Console.WriteLine($"Repository : {repository.Name}");
        Console.WriteLine($"Radice     : {repository.RootPath}");
        Console.WriteLine($"Git        : {repository.GitBranch ?? "-"} {repository.GitCommit ?? string.Empty}");
        Console.WriteLine($"File       : {stats.TotalFiles} ({stats.IndexedFiles} indicizzati, {stats.SkippedFiles} esclusi)");
        Console.WriteLine($"Righe      : {stats.TotalLines:N0}");
        Console.WriteLine($"Simboli    : {stats.Symbols:N0}   Relazioni: {stats.Relations:N0}");
        Console.WriteLine($"Soluzioni  : {stats.Solutions}   Progetti: {stats.Projects}");
        foreach (var language in stats.Languages)
        {
            Console.WriteLine($"  {LanguageCatalog.Describe(language.Language).DisplayName,-28} {language.Files,6} file {language.Lines,10:N0} righe  [{language.Support}]");
        }

        return 0;
    }

    private static async Task<int> ReposAsync(IServiceProvider provider, CancellationToken cancellationToken)
    {
        var store = provider.GetRequiredService<IIndexStore>();
        await store.InitializeAsync(cancellationToken);
        var repositories = await store.ListRepositoriesAsync(cancellationToken);
        if (repositories.Count == 0)
        {
            Console.WriteLine("Nessun repository indicizzato.");
        }

        foreach (var repository in repositories)
        {
            var scanned = repository.LastScanUtc?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "mai";
            Console.WriteLine($"{repository.Id,4}  {repository.Name,-30} {repository.RootPath}  (ultima scansione: {scanned})");
        }

        return 0;
    }

    private static async Task<int> RemoveAsync(IServiceProvider provider, CommandLine command, CancellationToken cancellationToken)
    {
        var repository = await RequireRepositoryAsync(provider, command.Argument(0, "cartella del repository"), cancellationToken);
        if (!command.Flag("yes"))
        {
            return Fail("Conferma con --yes: verrà eliminato solo l'indice locale, non i file sorgente.", 2);
        }

        await provider.GetRequiredService<IIndexStore>().DeleteRepositoryAsync(repository.Id, cancellationToken);
        Console.WriteLine($"Indice di '{repository.Name}' eliminato. I file sorgente non sono stati toccati.");
        return 0;
    }

    private static async Task<RepositoryInfo> RequireRepositoryAsync(IServiceProvider provider, string path, CancellationToken cancellationToken)
    {
        var (root, _, _) = IndexingService.ResolveTarget(path);
        var store = provider.GetRequiredService<IIndexStore>();
        await store.InitializeAsync(cancellationToken);
        return await store.FindRepositoryAsync(root, cancellationToken)
            ?? throw new InvalidOperationException($"Repository non indicizzato: {root}. Esegui prima 'lcia index \"{path}\"'.");
    }

    private static int ParseInt(string value, string name) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result >= 0
            ? result
            : throw new ArgumentException($"Valore non valido per --{name}: {value}");

    private static int Fail(string message, int code)
    {
        Console.Error.WriteLine("Errore: " + message);
        return code;
    }

    private static void TrySetUtf8Console()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // Console non disponibile (output reindirizzato): ignorato.
        }
    }

    /// <summary>IProgress che esegue la callback in modo sincrono (nessun contesto di sincronizzazione nella CLI).</summary>
    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        private readonly Lock _lock = new();

        public void Report(T value)
        {
            lock (_lock)
            {
                handler(value);
            }
        }
    }
}
