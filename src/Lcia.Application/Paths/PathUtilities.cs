namespace Lcia.Application;

/// <summary>Utilità sui percorsi con protezione da path traversal.</summary>
public static class PathUtilities
{
    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Percorso assoluto senza separatore finale (la radice di un volume resta invariata).</summary>
    public static string NormalizeRoot(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>Percorso relativo alla radice con separatore '/'.</summary>
    public static string ToRelative(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace('\\', '/');

    /// <summary>Normalizza un percorso relativo inserito dall'utente: '/' come separatore, senza '/' iniziali o finali.</summary>
    public static string NormalizeRelative(string relative) =>
        relative.Trim().Replace('\\', '/').Trim('/');

    /// <summary>True se <paramref name="candidate"/> coincide con <paramref name="parent"/> o si trova al suo interno.</summary>
    public static bool IsSameOrSubPath(string parent, string candidate)
    {
        var p = NormalizeRoot(parent);
        var c = NormalizeRoot(candidate);
        if (string.Equals(p, c, PathComparison))
        {
            return true;
        }

        var withSeparator = Path.EndsInDirectorySeparator(p) ? p : p + Path.DirectorySeparatorChar;
        return c.StartsWith(withSeparator, PathComparison);
    }

    /// <summary>
    /// Combina radice e percorso relativo verificando che il risultato resti all'interno della radice.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Se il percorso esce dalla radice (es. "../").</exception>
    public static string ResolveInside(string root, string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsSameOrSubPath(root, full))
        {
            throw new UnauthorizedAccessException($"Percorso fuori dalla cartella analizzata: {relativePath}");
        }

        return full;
    }

    /// <summary>True se il percorso relativo coincide con il prefisso o si trova nella sua sottocartella.</summary>
    public static bool HasRelativePrefix(string relativePath, string prefix)
    {
        if (prefix.Length == 0)
        {
            return true;
        }

        return relativePath.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
               relativePath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Cartella di un percorso relativo ("" per i file nella radice).</summary>
    public static string GetRelativeDirectory(string relativePath)
    {
        var index = relativePath.LastIndexOf('/');
        return index < 0 ? string.Empty : relativePath[..index];
    }

    /// <summary>Rende sicuro un nome di file sostituendo i caratteri non validi.</summary>
    public static string SanitizeFileName(string name)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
        foreach (var ch in "<>:\"/\\|?*")
        {
            invalid.Add(ch);
        }

        var chars = name.Trim().Select(ch => invalid.Contains(ch) || char.IsControl(ch) ? '_' : ch).ToArray();
        var result = new string(chars).Trim('.', ' ');
        return result.Length == 0 ? "export" : result;
    }
}
