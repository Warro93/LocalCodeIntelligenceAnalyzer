using Lcia.Domain;

namespace Lcia.Application;

public enum ExportFormat
{
    Text,
    Markdown,
}

/// <summary>
/// Ambito dell'esportazione. FULL e CUSTOM sono disponibili dalla Fase 2;
/// FEATURE, SQL e DEPENDENCY arriveranno con i rispettivi motori (Fasi 4-6).
/// </summary>
public enum ExportScope
{
    /// <summary>Tutti i file indicizzati che rispettano i filtri.</summary>
    Full,

    /// <summary>Solo i file selezionati esplicitamente (ricerca, explorer), poi filtrati.</summary>
    Custom,
}

/// <summary>Richiesta di FULL PROJECT CONTEXT EXPORT.</summary>
public sealed record ExportRequest
{
    public required long RepositoryId { get; init; }
    public required string OutputDirectory { get; init; }
    public ExportFormat Format { get; init; } = ExportFormat.Text;
    public ExportScope Scope { get; init; } = ExportScope.Full;

    /// <summary>Estensioni incluse (vuoto = tutte).</summary>
    public IReadOnlyCollection<string> IncludeExtensions { get; init; } = [];

    /// <summary>Cartelle incluse, come percorsi relativi (vuoto = tutte).</summary>
    public IReadOnlyCollection<string> IncludePathPrefixes { get; init; } = [];

    /// <summary>Cartelle escluse, come percorsi relativi.</summary>
    public IReadOnlyCollection<string> ExcludePathPrefixes { get; init; } = [];

    /// <summary>File scelti singolarmente (ambito Custom).</summary>
    public IReadOnlyCollection<string> SelectedFiles { get; init; } = [];

    public bool IncludeDirectoryTree { get; init; } = true;
    public bool IncludeFileList { get; init; } = true;
    public bool IncludeProjects { get; init; } = true;
    public bool IncludeSymbolIndex { get; init; } = true;
    public bool IncludeSourceCode { get; init; } = true;

    /// <summary>Antepone "N | " a ogni riga di codice per facilitare le citazioni.</summary>
    public bool IncludeLineNumbers { get; init; } = true;

    /// <summary>Se false il percorso assoluto della radice viene omesso (privacy: può contenere il nome utente).</summary>
    public bool IncludeAbsolutePaths { get; init; }

    /// <summary>Oscura i valori che sembrano segreti (predefinito: attivo).</summary>
    public bool RedactSecrets { get; init; } = true;

    /// <summary>Conferma esplicita richiesta per disattivare l'oscuramento.</summary>
    public bool UnredactedExportConfirmed { get; init; }

    /// <summary>Includi file tipicamente sensibili (secrets.json, .env, ...). Richiede conferma esplicita.</summary>
    public bool IncludeSensitiveFiles { get; init; }

    public bool SensitiveFilesConfirmed { get; init; }

    /// <summary>Dimensione massima per parte in token stimati (0 = file unico).</summary>
    public int MaxPartTokens { get; init; }

    /// <summary>Nome base dei file prodotti (senza estensione). Se nullo viene generato.</summary>
    public string? BaseFileName { get; init; }
}

public sealed record ExportFileEntry(string RelativePath, SourceLanguage Language, long SizeBytes, int LineCount);

public sealed record ExportPlan(
    IReadOnlyList<ExportFileEntry> Files,
    long EstimatedCharacters,
    long EstimatedTokens,
    int EstimatedParts,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> ExcludedSensitiveFiles);

public sealed record ExportPartInfo(int Number, string FilePath, long Characters, long EstimatedTokens, IReadOnlyList<string> Files);

/// <summary>Valore oscurato: percorso, riga e categoria. Il valore originale non viene mai conservato.</summary>
public sealed record ExportRedaction(string RelativePath, int Line, string Category);

public sealed record ExportResult(
    IReadOnlyList<ExportPartInfo> Parts,
    string? IndexFilePath,
    int FilesExported,
    long TotalCharacters,
    long EstimatedTokens,
    IReadOnlyList<ExportRedaction> Redactions,
    IReadOnlyList<string> ModifiedSinceIndex,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> ExcludedSensitiveFiles)
{
    public IEnumerable<string> WrittenFiles =>
        IndexFilePath is null ? Parts.Select(p => p.FilePath) : Parts.Select(p => p.FilePath).Prepend(IndexFilePath);
}

public sealed record ExportProgress(int Processed, int Total, string? CurrentFile);

/// <summary>Export Engine.</summary>
public interface IExportService
{
    /// <summary>Stima file, dimensione, token e numero di parti senza scrivere nulla.</summary>
    Task<ExportPlan> PlanAsync(ExportRequest request, CancellationToken cancellationToken = default);

    Task<ExportResult> ExportAsync(ExportRequest request, IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default);
}

public static class TokenEstimator
{
    /// <summary>
    /// Stima approssimativa: circa 4 caratteri per token. Il valore reale dipende dal modello
    /// e dal linguaggio; serve solo per dimensionare le esportazioni.
    /// </summary>
    public static long Estimate(long characters) => characters <= 0 ? 0 : (characters + 3) / 4;

    public static long ToCharacters(long tokens) => tokens <= 0 ? 0 : tokens * 4;
}
