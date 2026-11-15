namespace Lcia.Application;

public enum TextReadStatus
{
    Ok,
    Binary,
    TooLarge,
    Unreadable,
}

/// <summary>Risultato della lettura (sempre in sola lettura) di un file del repository.</summary>
public sealed record TextReadResult(
    TextReadStatus Status,
    string? Text,
    string? EncodingName,
    string? Sha256,
    int LineCount,
    long SizeBytes,
    string? Error)
{
    public static TextReadResult Failure(TextReadStatus status, long size, string? error, string? sha256 = null) =>
        new(status, null, null, sha256, 0, size, error);
}

/// <summary>Lettura dei file di testo con hash SHA-256, rilevamento binari e decodifica.</summary>
public interface ITextFileReader
{
    /// <summary>
    /// Legge il file senza bloccarlo per gli altri processi e senza mai modificarlo.
    /// </summary>
    TextReadResult Read(string fullPath, long maxBytes, CancellationToken cancellationToken);
}
