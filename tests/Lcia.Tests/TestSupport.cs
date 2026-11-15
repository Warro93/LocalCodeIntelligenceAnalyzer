using System.Security.Cryptography;
using Lcia.Application;
using Lcia.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Lcia.Tests;

/// <summary>Cartella temporanea eliminata al termine del test.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory(string prefix = "lcia-test")
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => parts.Aggregate(Path, System.IO.Path.Combine);

    public string WriteFile(string relativePath, string content)
    {
        var full = Combine(relativePath.Split('/'));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SqliteConnection.ClearAllPools();
                Thread.Sleep(150);
            }
        }
    }
}

/// <summary>Contenitore dei servizi reali con cartella dati isolata.</summary>
internal sealed class TestHost : IDisposable
{
    public TestHost()
    {
        Data = new TempDirectory("lcia-data");
        var services = new ServiceCollection();
        services.AddLocalCodeIntelligence(options =>
        {
            options.DataDirectory = Data.Path;
            options.EnableFileLogging = false;
        });
        Services = services.BuildServiceProvider();
    }

    public TempDirectory Data { get; }

    public ServiceProvider Services { get; }

    public T Get<T>()
        where T : notnull => Services.GetRequiredService<T>();

    public void Dispose()
    {
        Services.Dispose();
        SqliteConnection.ClearAllPools();
        Data.Dispose();
    }
}

/// <summary>IProgress che esegue la callback in modo sincrono.</summary>
internal sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}

internal static class SampleRepository
{
    public static string SourcePath => Path.Combine(AppContext.BaseDirectory, "samples", "DemoShop");

    /// <summary>Copia DemoShop in una cartella temporanea (i test possono modificarla liberamente).</summary>
    public static string CopyDemoShop(TempDirectory target)
    {
        var destination = target.Combine("DemoShop");
        CopyDirectory(SourcePath, destination);
        return destination;
    }

    public static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    /// <summary>Hash di tutti i file: serve a verificare che il repository non venga mai modificato.</summary>
    public static Dictionary<string, string> Snapshot(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                f => Path.GetRelativePath(root, f),
                f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    /// <summary>Numero (1-based) della prima riga che contiene il testo indicato.</summary>
    public static int LineOf(string text, string fragment)
    {
        var lines = TextLines.Split(text);
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains(fragment, StringComparison.Ordinal))
            {
                return i + 1;
            }
        }

        throw new InvalidOperationException($"Testo non trovato: {fragment}");
    }

    public static int LineOfFile(string path, string fragment) => LineOf(File.ReadAllText(path), fragment);
}
