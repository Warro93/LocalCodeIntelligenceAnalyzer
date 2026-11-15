using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lcia.App.Services;
using Lcia.Application;
using Lcia.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Lcia.App.ViewModels;

/// <summary>Impostazioni di scansione e interfaccia, salvate in locale.</summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly ISettingsStore _store;
    private readonly IIndexStore _index;
    private readonly AppPaths _paths;
    private readonly IDialogService _dialogs;
    private readonly ThemeService _theme;
    private readonly ILogger<SettingsViewModel> _logger;
    private bool _syncingTheme;

    [ObservableProperty]
    private string _excludedDirectoriesText = string.Empty;

    [ObservableProperty]
    private string _excludedPathsText = string.Empty;

    [ObservableProperty]
    private int _maxFileSizeKb;

    [ObservableProperty]
    private int _maxDepth;

    [ObservableProperty]
    private int _maxDegreeOfParallelism;

    [ObservableProperty]
    private bool _followSymbolicLinks;

    [ObservableProperty]
    private bool _useFluentTheme;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private Option<AppTheme>? _selectedThemeOption;

    public SettingsViewModel(ISettingsStore store, IIndexStore index, AppPaths paths, IDialogService dialogs, ThemeService theme, ILogger<SettingsViewModel> logger)
        : base("Impostazioni", "", "Scansione, esclusioni, limiti e aspetto")
    {
        _store = store;
        _index = index;
        _paths = paths;
        _dialogs = dialogs;
        _theme = theme;
        _logger = logger;
        Load(store.Load());
        SyncThemeSelection();
        _theme.ThemeChanged += (_, _) => SyncThemeSelection();
    }

    public IReadOnlyList<Option<AppTheme>> ThemeOptions { get; } =
    [
        new(AppTheme.System, "Come Windows (chiaro o scuro)"),
        new(AppTheme.Light, "Chiaro"),
        new(AppTheme.Dark, "Scuro"),
    ];

    /// <summary>Il tema si può scegliere solo con lo stile Fluent attivo.</summary>
    public bool IsThemeSelectable => _theme.FluentEnabled;

    public string ThemeNote => _theme.FluentEnabled
        ? "Il tema cambia subito e viene ricordato. Scorciatoia: Ctrl+Maiusc+T."
        : "Il tema scuro richiede lo stile Fluent: attivalo qui sotto, salva e riavvia l'applicazione.";

    public string DataDirectory => _paths.DataDirectory;

    public string DatabasePath => _index.DatabasePath;

    public string SettingsPath => _store.SettingsPath;

    public int ProcessorCount => Environment.ProcessorCount;

    public IReadOnlyList<SupportRow> SupportMatrix { get; } = Lcia.Domain.LanguageCatalog.All
        .Select(l => new SupportRow(l.DisplayName, DashboardViewModel.SupportLabel(l.CurrentSupport), l.SupportDescription))
        .ToArray();

    partial void OnSelectedThemeOptionChanged(Option<AppTheme>? value)
    {
        if (_syncingTheme || value is null || value.Value == _theme.Theme)
        {
            return;
        }

        _theme.SetTheme(value.Value);
    }

    private void SyncThemeSelection()
    {
        _syncingTheme = true;
        try
        {
            SelectedThemeOption = ThemeOptions.FirstOrDefault(o => o.Value == _theme.Theme) ?? ThemeOptions[0];
            OnPropertyChanged(nameof(IsThemeSelectable));
            OnPropertyChanged(nameof(ThemeNote));
        }
        finally
        {
            _syncingTheme = false;
        }
    }

    [RelayCommand]
    private void Save()
    {
        var settings = _store.Load();
        settings.ExcludedDirectoryNames = Split(ExcludedDirectoriesText);
        settings.ExcludedRelativePaths = Split(ExcludedPathsText);
        settings.MaxFileSizeKb = Math.Clamp(MaxFileSizeKb, 16, 1024 * 1024);
        settings.MaxDepth = Math.Clamp(MaxDepth, 1, 512);
        settings.MaxDegreeOfParallelism = Math.Clamp(MaxDegreeOfParallelism, 1, 64);
        settings.FollowSymbolicLinks = FollowSymbolicLinks;
        settings.UseFluentTheme = UseFluentTheme;
        try
        {
            _store.Save(settings);
            Load(settings);
            StatusText = "Impostazioni salvate. Le nuove esclusioni valgono dalla prossima indicizzazione; lo stile Fluent al prossimo avvio.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Salvataggio impostazioni non riuscito.");
            StatusText = "Salvataggio non riuscito: " + ex.Message;
        }
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        if (!_dialogs.Confirm("Ripristina impostazioni", "Ripristinare i valori predefiniti di scansione e aspetto?"))
        {
            return;
        }

        var current = _store.Load();
        var defaults = new AppSettings
        {
            RecentPaths = current.RecentPaths,
            LastExportDirectory = current.LastExportDirectory,
            DefaultMaxPartTokens = current.DefaultMaxPartTokens,
        };
        Load(defaults);
        StatusText = "Valori predefiniti caricati: premi Salva per confermarli.";
    }

    [RelayCommand]
    private void OpenDataFolder() => _dialogs.OpenFolder(_paths.DataDirectory);

    private void Load(AppSettings settings)
    {
        ExcludedDirectoriesText = string.Join(", ", settings.ExcludedDirectoryNames);
        ExcludedPathsText = string.Join(Environment.NewLine, settings.ExcludedRelativePaths);
        MaxFileSizeKb = settings.MaxFileSizeKb;
        MaxDepth = settings.MaxDepth;
        MaxDegreeOfParallelism = settings.MaxDegreeOfParallelism;
        FollowSymbolicLinks = settings.FollowSymbolicLinks;
        UseFluentTheme = settings.UseFluentTheme;
    }

    private static List<string> Split(string text) =>
        text.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
