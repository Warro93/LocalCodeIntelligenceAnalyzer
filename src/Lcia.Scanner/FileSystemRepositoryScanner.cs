using Lcia.Application;
using Lcia.Domain;

namespace Lcia.Scanner;

/// <summary>
/// Visita iterativa dell'albero delle cartelle (nessuna ricorsione profonda sullo stack),
/// con esclusioni configurabili, limite di profondità e protezione da collegamenti
/// simbolici o junction che portano fuori dalla cartella selezionata.
/// Non legge il contenuto dei file e non modifica mai nulla.
/// </summary>
public sealed class FileSystemRepositoryScanner : IRepositoryScanner
{
    public Task<DiscoveryResult> DiscoverAsync(string rootPath, ScanOptions options, CancellationToken cancellationToken = default) =>
        Task.Run(() => Discover(rootPath, options, cancellationToken), cancellationToken);

    public DiscoveryResult Discover(string rootPath, ScanOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var root = PathUtilities.NormalizeRoot(rootPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Cartella non trovata: {root}");
        }

        var excludedNames = new HashSet<string>(options.ExcludedDirectoryNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);
        var excludedPrefixes = options.ExcludedRelativePaths.Select(PathUtilities.NormalizeRelative).Where(p => p.Length > 0).ToArray();
        var extensions = new HashSet<string>(options.IncludedExtensions.Select(NormalizeExtension), StringComparer.OrdinalIgnoreCase);
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0,
        };

        var files = new List<DiscoveredFile>();
        var warnings = new List<ScanWarning>();
        var visitedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        var directoriesVisited = 0;
        var directoriesExcluded = 0;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, depth) = pending.Pop();
            directoriesVisited++;

            List<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(directory).EnumerateFileSystemInfos("*", enumeration).ToList();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                warnings.Add(new ScanWarning(Relative(root, directory), $"Cartella non leggibile: {ex.Message}"));
                continue;
            }

            foreach (var entry in entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                var relative = Relative(root, entry.FullName);
                var isDirectory = entry is DirectoryInfo;
                var linkTarget = GetLinkTarget(entry);
                string? resolvedTarget = null;

                if (linkTarget is not null)
                {
                    if (!options.FollowSymbolicLinks)
                    {
                        warnings.Add(new ScanWarning(relative, "Collegamento simbolico o junction ignorato (impostazione 'Segui collegamenti' disattivata)."));
                        continue;
                    }

                    resolvedTarget = ResolveLink(entry);
                    if (resolvedTarget is null || !PathUtilities.IsSameOrSubPath(root, resolvedTarget))
                    {
                        warnings.Add(new ScanWarning(relative, "Collegamento che punta fuori dalla cartella selezionata: ignorato per sicurezza."));
                        continue;
                    }
                }

                if (isDirectory)
                {
                    if (excludedNames.Contains(entry.Name) || IsExcluded(relative, excludedPrefixes))
                    {
                        directoriesExcluded++;
                        continue;
                    }

                    if (depth + 1 > options.MaxDepth)
                    {
                        warnings.Add(new ScanWarning(relative, $"Profondità massima ({options.MaxDepth}) superata: cartella ignorata."));
                        continue;
                    }

                    if (resolvedTarget is not null && !visitedTargets.Add(PathUtilities.NormalizeRoot(resolvedTarget)))
                    {
                        warnings.Add(new ScanWarning(relative, "Collegamento a una cartella già visitata: ignorato per evitare cicli."));
                        continue;
                    }

                    if (resolvedTarget is null)
                    {
                        visitedTargets.Add(PathUtilities.NormalizeRoot(entry.FullName));
                    }

                    pending.Push((entry.FullName, depth + 1));
                    continue;
                }

                if (IsExcluded(relative, excludedPrefixes))
                {
                    continue;
                }

                var extension = Path.GetExtension(entry.Name);
                if (string.IsNullOrEmpty(extension) || !extensions.Contains(extension))
                {
                    continue;
                }

                try
                {
                    var info = resolvedTarget is not null ? new FileInfo(resolvedTarget) : (FileInfo)entry;
                    files.Add(new DiscoveredFile(
                        entry.FullName,
                        relative,
                        extension.ToLowerInvariant(),
                        LanguageCatalog.FromExtension(extension),
                        info.Length,
                        info.LastWriteTimeUtc));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warnings.Add(new ScanWarning(relative, $"Metadati non leggibili: {ex.Message}"));
                }
            }
        }

        files.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
        return new DiscoveryResult(files, warnings, directoriesVisited, directoriesExcluded);
    }

    private static string Relative(string root, string fullPath)
    {
        var relative = PathUtilities.ToRelative(root, fullPath);
        return relative == "." ? string.Empty : relative;
    }

    private static string NormalizeExtension(string extension)
    {
        var trimmed = extension.Trim();
        return trimmed.StartsWith('.') ? trimmed : "." + trimmed;
    }

    private static bool IsExcluded(string relativePath, string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (PathUtilities.HasRelativePrefix(relativePath, prefix))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Restituisce la destinazione se l'elemento è un collegamento simbolico o una junction.
    /// Altri reparse point (es. file OneDrive solo online) sono trattati come elementi normali.
    /// </summary>
    private static string? GetLinkTarget(FileSystemInfo entry)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            return null;
        }

        try
        {
            return entry.LinkTarget;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ResolveLink(FileSystemInfo entry)
    {
        try
        {
            return entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
