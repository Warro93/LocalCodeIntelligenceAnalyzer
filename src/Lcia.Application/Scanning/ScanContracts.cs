using Lcia.Domain;

namespace Lcia.Application;

/// <summary>Opzioni di scansione (tutte configurabili dall'utente).</summary>
public sealed record ScanOptions
{
    /// <summary>Cartelle escluse per nome, in qualsiasi posizione dell'albero.</summary>
    public static readonly IReadOnlyList<string> DefaultExcludedDirectoryNames =
    [
        ".git", ".vs", ".vscode", ".idea", ".svn", ".hg",
        "bin", "obj", "node_modules", "packages", "dist", "build", "artifacts", "TestResults",
        "__pycache__", ".venv", "venv", ".pytest_cache", ".gradle", ".cache",
        "tmp", "temp", ".tmp",
    ];

    public IReadOnlyCollection<string> ExcludedDirectoryNames { get; init; } = DefaultExcludedDirectoryNames;

    /// <summary>Percorsi relativi esclusi (prefissi, separatore '/').</summary>
    public IReadOnlyCollection<string> ExcludedRelativePaths { get; init; } = [];

    /// <summary>Estensioni incluse (con il punto). Le altre vengono ignorate.</summary>
    public IReadOnlyCollection<string> IncludedExtensions { get; init; } = LanguageCatalog.DefaultExtensions;

    /// <summary>Dimensione massima indicizzata per file; i file più grandi sono elencati ma non letti.</summary>
    public long MaxFileSizeBytes { get; init; } = 2L * 1024 * 1024;

    public int MaxDepth { get; init; } = 64;

    /// <summary>
    /// Se true segue collegamenti simbolici e junction, ma solo se puntano all'interno della cartella selezionata.
    /// </summary>
    public bool FollowSymbolicLinks { get; init; }

    public int MaxDegreeOfParallelism { get; init; } = Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>Se true rielabora tutti i file anche se invariati.</summary>
    public bool ForceReindex { get; init; }
}

/// <summary>File individuato dallo scanner (solo metadati).</summary>
public sealed record DiscoveredFile(
    string FullPath,
    string RelativePath,
    string Extension,
    SourceLanguage Language,
    long SizeBytes,
    DateTime LastWriteUtc);

/// <summary>Avviso non bloccante prodotto durante la scansione.</summary>
public sealed record ScanWarning(string RelativePath, string Message)
{
    public override string ToString() => string.IsNullOrEmpty(RelativePath) ? Message : $"{RelativePath}: {Message}";
}

public sealed record DiscoveryResult(
    IReadOnlyList<DiscoveredFile> Files,
    IReadOnlyList<ScanWarning> Warnings,
    int DirectoriesVisited,
    int DirectoriesExcluded);

/// <summary>Repository Scanner: individua i file da analizzare. Non modifica mai il file system.</summary>
public interface IRepositoryScanner
{
    Task<DiscoveryResult> DiscoverAsync(string rootPath, ScanOptions options, CancellationToken cancellationToken = default);
}

/// <summary>Riconosce soluzioni e progetti (.sln, .slnx, .csproj, ...) tra i file individuati.</summary>
public interface IWorkspaceDiscovery
{
    IReadOnlyList<ProjectInfo> Discover(
        string rootPath,
        IReadOnlyList<DiscoveredFile> files,
        ICollection<ScanWarning> warnings,
        CancellationToken cancellationToken = default);
}
