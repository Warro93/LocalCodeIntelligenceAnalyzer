using Lcia.Domain;

namespace Lcia.Application;

/// <summary>Impronta di un file indicizzato, usata per la scansione incrementale.</summary>
public sealed record FileFingerprint(
    long FileId,
    long SizeBytes,
    long LastWriteTicks,
    string? Sha256,
    FileIndexStatus Status,
    int IndexVersion);

public enum FileWorkKind
{
    /// <summary>Contenuto (ri)analizzato: sostituisce simboli, relazioni e testo indicizzato.</summary>
    Full,

    /// <summary>Contenuto invariato (stesso hash): aggiorna solo dimensione e data.</summary>
    MetadataOnly,
}

/// <summary>Unità di lavoro prodotta dall'elaborazione parallela e scritta in batch nell'indice.</summary>
public sealed record FileWorkItem(FileWorkKind Kind, IndexedFile File, string? Content, SourceAnalysisResult? Analysis);

public sealed record LanguageStatistics(SourceLanguage Language, int Files, long Lines, long Bytes, SupportLevel Support);

public sealed record RepositoryStatistics(
    int TotalFiles,
    int IndexedFiles,
    int SkippedFiles,
    long TotalLines,
    long TotalBytes,
    int Symbols,
    int Relations,
    int Solutions,
    int Projects,
    IReadOnlyList<LanguageStatistics> Languages,
    IReadOnlyDictionary<FileIndexStatus, int> ByStatus)
{
    public static RepositoryStatistics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, [], new Dictionary<FileIndexStatus, int>());
}

/// <summary>
/// Indice locale (SQLite + FTS5). Contiene solo metadati e testo dei file indicizzati:
/// i sorgenti originali non vengono mai modificati.
/// </summary>
public interface IIndexStore
{
    string DatabasePath { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<RepositoryInfo> GetOrCreateRepositoryAsync(string rootPath, string name, string? entryPath, CancellationToken cancellationToken = default);

    Task<RepositoryInfo?> FindRepositoryAsync(string rootPath, CancellationToken cancellationToken = default);

    Task<RepositoryInfo?> GetRepositoryAsync(long repositoryId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RepositoryInfo>> ListRepositoriesAsync(CancellationToken cancellationToken = default);

    Task UpdateRepositoryScanInfoAsync(long repositoryId, DateTimeOffset scanUtc, string? entryPath, GitHeadInfo? git, CancellationToken cancellationToken = default);

    /// <summary>Elimina l'indice del repository (mai i file sorgente).</summary>
    Task DeleteRepositoryAsync(long repositoryId, CancellationToken cancellationToken = default);

    Task<long> BeginScanRunAsync(long repositoryId, DateTimeOffset startedUtc, CancellationToken cancellationToken = default);

    Task CompleteScanRunAsync(ScanRun run, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScanRun>> ListScanRunsAsync(long repositoryId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Marca come interrotte le esecuzioni rimaste "in corso" (es. applicazione chiusa durante la scansione).</summary>
    Task<int> MarkInterruptedScanRunsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, FileFingerprint>> GetFileFingerprintsAsync(long repositoryId, CancellationToken cancellationToken = default);

    Task SaveFilesAsync(long repositoryId, IReadOnlyList<FileWorkItem> items, CancellationToken cancellationToken = default);

    Task RemoveFilesAsync(long repositoryId, IReadOnlyCollection<string> relativePaths, CancellationToken cancellationToken = default);

    Task ReplaceProjectsAsync(long repositoryId, IReadOnlyList<ProjectInfo> projects, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IndexedFile>> ListFilesAsync(long repositoryId, CancellationToken cancellationToken = default);

    Task<IndexedFile?> GetFileAsync(long repositoryId, string relativePath, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectInfo>> ListProjectsAsync(long repositoryId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CodeSymbol>> ListSymbolsAsync(long fileId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SymbolRelation>> ListRelationsAsync(long fileId, CancellationToken cancellationToken = default);

    Task<RepositoryStatistics> GetStatisticsAsync(long repositoryId, CancellationToken cancellationToken = default);
}

public enum IndexingPhase
{
    Preparing,
    Discovering,
    Comparing,
    Analyzing,
    Projects,
    Completed,
}

public sealed record IndexingProgress(IndexingPhase Phase, int Processed, int Total, string? CurrentPath, string Message)
{
    public double Percent => Total <= 0 ? 0 : Math.Clamp(Processed * 100.0 / Total, 0, 100);
}

/// <summary>Richiesta di indicizzazione: cartella oppure file .sln/.slnx.</summary>
public sealed record IndexingRequest(string Path, ScanOptions Options);

public sealed record IndexingResult(
    RepositoryInfo Repository,
    ScanRun Run,
    IReadOnlyList<ScanWarning> Warnings,
    TimeSpan Elapsed);
