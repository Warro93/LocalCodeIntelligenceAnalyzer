namespace Lcia.Application;

/// <summary>Stato di HEAD letto in sola lettura dalla cartella .git.</summary>
public sealed record GitHeadInfo(string? Branch, string? Commit, bool IsDetached)
{
    public string ShortCommit => Commit is { Length: > 12 } c ? c[..12] : Commit ?? string.Empty;
}

/// <summary>
/// Lettura dei metadati Git senza eseguire comandi e senza modificare il repository.
/// </summary>
public interface IGitMetadataReader
{
    GitHeadInfo? TryRead(string rootPath);
}
