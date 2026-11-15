using Lcia.Application;

namespace Lcia.Infrastructure;

/// <summary>
/// Legge branch e commit correnti direttamente dai file di .git (HEAD, refs, packed-refs),
/// senza eseguire Git e senza alcuna scrittura. Supporta anche worktree e submodule (file ".git").
/// </summary>
public sealed class GitHeadReader : IGitMetadataReader
{
    public GitHeadInfo? TryRead(string rootPath)
    {
        try
        {
            var gitDirectory = ResolveGitDirectory(rootPath);
            if (gitDirectory is null)
            {
                return null;
            }

            var headPath = Path.Combine(gitDirectory, "HEAD");
            if (!File.Exists(headPath))
            {
                return null;
            }

            var head = File.ReadAllText(headPath).Trim();
            if (head.StartsWith("ref:", StringComparison.Ordinal))
            {
                var referenceName = head[4..].Trim();
                const string headsPrefix = "refs/heads/";
                string? branch = referenceName.StartsWith(headsPrefix, StringComparison.Ordinal) ? referenceName[headsPrefix.Length..] : referenceName;
                if (branch == ".invalid")
                {
                    // Formato "reftable": il branch reale non è leggibile senza Git.
                    branch = null;
                }

                return new GitHeadInfo(branch, ReadReference(gitDirectory, referenceName), false);
            }

            return new GitHeadInfo(null, IsHash(head) ? head : null, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? ResolveGitDirectory(string root)
    {
        var dotGit = Path.Combine(root, ".git");
        if (Directory.Exists(dotGit))
        {
            return dotGit;
        }

        if (!File.Exists(dotGit))
        {
            return null;
        }

        var content = File.ReadAllText(dotGit).Trim();
        const string prefix = "gitdir:";
        if (!content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var target = content[prefix.Length..].Trim();
        var full = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(root, target));
        return Directory.Exists(full) ? full : null;
    }

    private static string? ReadReference(string gitDirectory, string referenceName)
    {
        if (referenceName.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(referenceName))
        {
            return null;
        }

        var candidates = new List<string> { gitDirectory };
        var commonDirFile = Path.Combine(gitDirectory, "commondir");
        if (File.Exists(commonDirFile))
        {
            var common = File.ReadAllText(commonDirFile).Trim();
            candidates.Add(Path.GetFullPath(Path.IsPathRooted(common) ? common : Path.Combine(gitDirectory, common)));
        }

        foreach (var directory in candidates)
        {
            var loose = Path.Combine(directory, referenceName.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(loose))
            {
                var value = File.ReadAllText(loose).Trim();
                return IsHash(value) ? value : null;
            }

            var packed = Path.Combine(directory, "packed-refs");
            if (!File.Exists(packed))
            {
                continue;
            }

            foreach (var line in File.ReadLines(packed))
            {
                if (line.Length == 0 || line[0] == '#' || line[0] == '^')
                {
                    continue;
                }

                var space = line.IndexOf(' ');
                if (space > 0 && line[(space + 1)..].Trim() == referenceName)
                {
                    var hash = line[..space];
                    return IsHash(hash) ? hash : null;
                }
            }
        }

        return null;
    }

    private static bool IsHash(string value) =>
        value.Length is >= 40 and <= 64 && value.All(Uri.IsHexDigit);
}
