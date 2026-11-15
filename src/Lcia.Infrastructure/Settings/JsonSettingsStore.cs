using System.Text.Json;
using Lcia.Application;
using Microsoft.Extensions.Logging;

namespace Lcia.Infrastructure;

/// <summary>Impostazioni in JSON con scrittura atomica (file temporaneo + sostituzione).</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly ILogger<JsonSettingsStore> _logger;
    private readonly Lock _lock = new();

    public JsonSettingsStore(string settingsPath, ILogger<JsonSettingsStore> logger)
    {
        SettingsPath = settingsPath;
        _logger = logger;
    }

    public string SettingsPath { get; }

    public AppSettings Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return new AppSettings();
                }

                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger.LogWarning(ex, "Impostazioni non leggibili ({Path}): uso i valori predefiniti.", SettingsPath);
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_lock)
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var temporary = SettingsPath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
                File.Move(temporary, SettingsPath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Impossibile salvare le impostazioni in {Path}", SettingsPath);
                throw;
            }
        }
    }
}
