using Lcia.Application;
using Lcia.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lcia.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void ThemeIsSavedAsTextAndReloaded()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("settings.json");
        var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        var settings = store.Load();
        Assert.Equal(AppTheme.System, settings.Theme);

        settings.Theme = AppTheme.Dark;
        store.Save(settings);

        Assert.Contains("\"Dark\"", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal(AppTheme.Dark, store.Load().Theme);
    }

    [Fact]
    public void SettingsWithoutThemeUseSystemTheme()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteFile("settings.json", "{ \"MaxFileSizeKb\": 512, \"UseFluentTheme\": true }");
        var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        var settings = store.Load();
        Assert.Equal(512, settings.MaxFileSizeKb);
        Assert.Equal(AppTheme.System, settings.Theme);
    }
}
