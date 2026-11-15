namespace Lcia.Domain;

/// <summary>Repository o cartella indicizzata.</summary>
public sealed record RepositoryInfo
{
    public long Id { get; init; }

    /// <summary>Percorso assoluto normalizzato della cartella radice.</summary>
    public required string RootPath { get; init; }

    public required string Name { get; init; }

    /// <summary>Soluzione (.sln/.slnx) selezionata come punto di ingresso, se presente.</summary>
    public string? EntryPath { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset? LastScanUtc { get; init; }

    public string? GitBranch { get; init; }

    public string? GitCommit { get; init; }

    public string DisplayName => string.IsNullOrEmpty(EntryPath) ? Name : $"{Name} ({Path.GetFileName(EntryPath)})";
}

public enum ScanRunStatus
{
    Running,
    Completed,
    Cancelled,
    Failed,
}

/// <summary>Esecuzione di una scansione/indicizzazione, conservata per diagnostica e ripresa.</summary>
public sealed record ScanRun
{
    public long Id { get; init; }
    public long RepositoryId { get; init; }
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset? FinishedUtc { get; init; }
    public ScanRunStatus Status { get; init; }
    public int FilesSeen { get; init; }
    public int FilesIndexed { get; init; }
    public int FilesUnchanged { get; init; }
    public int FilesRemoved { get; init; }
    public int FilesSkipped { get; init; }
    public int Errors { get; init; }
    public int WarningCount { get; init; }

    /// <summary>Primi avvisi registrati (uno per riga), senza contenuti dei file.</summary>
    public string? WarningsText { get; init; }

    public string? Message { get; init; }

    public TimeSpan? Duration => FinishedUtc is { } end ? end - StartedUtc : null;
}
