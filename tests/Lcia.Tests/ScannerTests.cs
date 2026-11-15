using Lcia.Application;
using Lcia.Domain;
using Lcia.Infrastructure;
using Lcia.Scanner;

namespace Lcia.Tests;

public sealed class ScannerTests
{
    private static TempDirectory CreateTree()
    {
        var temp = new TempDirectory();
        temp.WriteFile("src/App/Program.cs", "class P {}");
        temp.WriteFile("src/App/Data/Query.sql", "SELECT 1");
        temp.WriteFile("src/App/appsettings.json", "{}");
        temp.WriteFile("src/App/bin/Debug/Generated.cs", "class G {}");
        temp.WriteFile("src/App/obj/Temp.cs", "class T {}");
        temp.WriteFile("web/node_modules/lib/index.js", "x");
        temp.WriteFile(".git/config", "[core]");
        temp.WriteFile("docs/readme.md", "# doc");
        temp.WriteFile("assets/logo.png", "not really a png");
        temp.WriteFile("deep/a/b/c/Deep.cs", "class D {}");
        return temp;
    }

    [Fact]
    public void FindsSupportedFilesAndSkipsDefaultExclusions()
    {
        using var temp = CreateTree();
        var result = new FileSystemRepositoryScanner().Discover(temp.Path, new ScanOptions(), CancellationToken.None);
        var paths = result.Files.Select(f => f.RelativePath).ToList();

        Assert.Contains("src/App/Program.cs", paths);
        Assert.Contains("src/App/Data/Query.sql", paths);
        Assert.Contains("src/App/appsettings.json", paths);
        Assert.Contains("docs/readme.md", paths);
        Assert.Contains("deep/a/b/c/Deep.cs", paths);
        Assert.DoesNotContain(paths, p => p.Contains("/bin/", StringComparison.Ordinal) || p.Contains("/obj/", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, p => p.Contains("node_modules", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, p => p.StartsWith(".git", StringComparison.Ordinal));
        Assert.DoesNotContain("assets/logo.png", paths);

        Assert.Equal(SourceLanguage.TSql, result.Files.Single(f => f.RelativePath == "src/App/Data/Query.sql").Language);
        Assert.Equal(SourceLanguage.CSharp, result.Files.Single(f => f.RelativePath == "src/App/Program.cs").Language);
        Assert.True(result.DirectoriesExcluded >= 4);
    }

    [Fact]
    public void AppliesCustomExclusionsAndDepthLimit()
    {
        using var temp = CreateTree();
        var options = new ScanOptions { ExcludedRelativePaths = ["docs"], MaxDepth = 2 };
        var result = new FileSystemRepositoryScanner().Discover(temp.Path, options, CancellationToken.None);
        var paths = result.Files.Select(f => f.RelativePath).ToList();

        Assert.DoesNotContain("docs/readme.md", paths);
        Assert.DoesNotContain("deep/a/b/c/Deep.cs", paths);
        Assert.Contains(result.Warnings, w => w.Message.Contains("Profondità massima", StringComparison.Ordinal));
        Assert.Contains("src/App/Program.cs", paths);
    }

    [Fact]
    public void HonorsExtensionFilter()
    {
        using var temp = CreateTree();
        var options = new ScanOptions { IncludedExtensions = ["cs"] };
        var result = new FileSystemRepositoryScanner().Discover(temp.Path, options, CancellationToken.None);
        Assert.All(result.Files, f => Assert.Equal(".cs", f.Extension));
    }

    [Fact]
    public void DoesNotFollowLinksOutsideTheRoot()
    {
        using var temp = CreateTree();
        using var outside = new TempDirectory("lcia-outside");
        outside.WriteFile("Secret.cs", "class S {}");
        var link = temp.Combine("linked");
        try
        {
            Directory.CreateSymbolicLink(link, outside.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Creare collegamenti simbolici può richiedere privilegi (Windows senza modalità sviluppatore).
            return;
        }

        foreach (var follow in new[] { false, true })
        {
            var result = new FileSystemRepositoryScanner().Discover(temp.Path, new ScanOptions { FollowSymbolicLinks = follow }, CancellationToken.None);
            Assert.DoesNotContain(result.Files, f => f.RelativePath.StartsWith("linked", StringComparison.Ordinal));
            Assert.Contains(result.Warnings, w => w.RelativePath == "linked");
        }
    }

    [Fact]
    public void ReaderComputesHashAndDetectsBinary()
    {
        using var temp = new TempDirectory();
        var text = temp.WriteFile("a.cs", "line1\nline2\n");
        var binary = temp.Combine("b.json");
        File.WriteAllBytes(binary, [0x7B, 0x00, 0x01, 0x7D]);

        var reader = new TextFileReader();
        var ok = reader.Read(text, 1024, CancellationToken.None);
        Assert.Equal(TextReadStatus.Ok, ok.Status);
        Assert.Equal(2, ok.LineCount);
        Assert.Equal(64, ok.Sha256!.Length);

        Assert.Equal(TextReadStatus.Binary, reader.Read(binary, 1024, CancellationToken.None).Status);
        Assert.Equal(TextReadStatus.TooLarge, reader.Read(text, 4, CancellationToken.None).Status);
        Assert.Equal(TextReadStatus.Unreadable, reader.Read(temp.Combine("missing.cs"), 1024, CancellationToken.None).Status);
    }
}
