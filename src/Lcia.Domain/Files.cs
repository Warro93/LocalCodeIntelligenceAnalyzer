namespace Lcia.Domain;

public enum FileIndexStatus
{
    /// <summary>Contenuto testuale indicizzato.</summary>
    Indexed,

    /// <summary>Oltre il limite di dimensione configurato: elencato ma non indicizzato.</summary>
    TooLarge,

    /// <summary>Contenuto binario: escluso da indicizzazione ed esportazione.</summary>
    Binary,

    /// <summary>Impossibile leggere il file (permessi, lock, errore di I/O).</summary>
    Unreadable,
}

/// <summary>File presente nell'indice locale.</summary>
public sealed record IndexedFile
{
    public long Id { get; init; }
    public long RepositoryId { get; init; }
    public long? ProjectId { get; init; }

    /// <summary>Percorso relativo alla radice, sempre con separatore '/'.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Estensione in minuscolo, con il punto iniziale.</summary>
    public required string Extension { get; init; }

    public SourceLanguage Language { get; init; }
    public long SizeBytes { get; init; }
    public DateTime LastWriteUtc { get; init; }
    public string? Sha256 { get; init; }
    public int LineCount { get; init; }
    public string? Encoding { get; init; }
    public FileIndexStatus Status { get; init; }
    public string? StatusDetail { get; init; }
    public SupportLevel AnalysisLevel { get; init; }

    /// <summary>Versione del formato di analisi con cui il file è stato indicizzato.</summary>
    public int IndexVersion { get; init; }

    public DateTimeOffset IndexedUtc { get; init; }

    public string FileName
    {
        get
        {
            var index = RelativePath.LastIndexOf('/');
            return index < 0 ? RelativePath : RelativePath[(index + 1)..];
        }
    }
}
