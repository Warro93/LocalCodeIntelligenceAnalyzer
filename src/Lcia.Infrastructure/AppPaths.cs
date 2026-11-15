namespace Lcia.Infrastructure;

/// <summary>
/// Percorsi dei dati locali dell'applicazione (indice, log, impostazioni).
/// Predefinito: %LOCALAPPDATA%\LocalCodeIntelligenceAnalyzer. Sovrascrivibile con
/// la variabile d'ambiente LCIA_DATA_DIR o con l'opzione --data-dir della CLI.
/// </summary>
public sealed class AppPaths
{
    public const string DataDirectoryEnvironmentVariable = "LCIA_DATA_DIR";

    public AppPaths(string? dataDirectory = null)
    {
        var configured = dataDirectory ?? Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
        DataDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalCodeIntelligenceAnalyzer")
            : configured);
    }

    public string DataDirectory { get; }

    public string IndexDirectory => Path.Combine(DataDirectory, "index");

    public string IndexDatabasePath => Path.Combine(IndexDirectory, "lcia-index.db");

    public string LogsDirectory => Path.Combine(DataDirectory, "logs");

    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(IndexDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
