using System.Text.Json.Serialization;

namespace Lcia.Application;

/// <summary>Tema dell'interfaccia.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AppTheme>))]
public enum AppTheme
{
    /// <summary>Segue l'impostazione chiaro/scuro di Windows.</summary>
    System,

    Light,

    Dark,
}

/// <summary>Impostazioni utente, salvate in locale (JSON) nella cartella dati dell'applicazione.</summary>
public sealed class AppSettings
{
    public List<string> ExcludedDirectoryNames { get; set; } = [.. ScanOptions.DefaultExcludedDirectoryNames];

    public List<string> ExcludedRelativePaths { get; set; } = [];

    public int MaxFileSizeKb { get; set; } = 2048;

    public int MaxDepth { get; set; } = 64;

    public bool FollowSymbolicLinks { get; set; }

    public int MaxDegreeOfParallelism { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);

    public bool UseFluentTheme { get; set; } = true;

    public AppTheme Theme { get; set; } = AppTheme.System;

    public string? LastExportDirectory { get; set; }

    public int DefaultMaxPartTokens { get; set; }

    public List<string> RecentPaths { get; set; } = [];

    public ScanOptions ToScanOptions() => new()
    {
        ExcludedDirectoryNames = ExcludedDirectoryNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToArray(),
        ExcludedRelativePaths = ExcludedRelativePaths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(PathUtilities.NormalizeRelative).ToArray(),
        MaxFileSizeBytes = Math.Clamp(MaxFileSizeKb, 1, 1024 * 1024) * 1024L,
        MaxDepth = Math.Clamp(MaxDepth, 1, 512),
        FollowSymbolicLinks = FollowSymbolicLinks,
        MaxDegreeOfParallelism = Math.Clamp(MaxDegreeOfParallelism, 1, 64),
    };

    public void AddRecentPath(string path, int max = 10)
    {
        RecentPaths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentPaths.Insert(0, path);
        if (RecentPaths.Count > max)
        {
            RecentPaths.RemoveRange(max, RecentPaths.Count - max);
        }
    }
}

public interface ISettingsStore
{
    string SettingsPath { get; }

    AppSettings Load();

    void Save(AppSettings settings);
}
