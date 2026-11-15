using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Lcia.App.Services;

namespace Lcia.App.ViewModels;

/// <summary>Finestra principale: navigazione tra le pagine, tema, scorciatoie e stato condiviso.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ExplorerViewModel _explorer;
    private readonly SearchViewModel _search;
    private readonly ExportViewModel _export;
    private readonly HelpViewModel _help;
    private readonly ThemeService _theme;

    [ObservableProperty]
    private PageViewModel _currentPage = null!;

    public MainViewModel(
        WorkspaceContext context,
        ExportSelection selection,
        ThemeService theme,
        DashboardViewModel dashboard,
        ExplorerViewModel explorer,
        SearchViewModel search,
        ExportViewModel export,
        LogsViewModel logs,
        SettingsViewModel settings,
        HelpViewModel help)
    {
        Context = context;
        Selection = selection;
        _theme = theme;
        _explorer = explorer;
        _search = search;
        _export = export;
        _help = help;
        Pages = new ObservableCollection<PageViewModel> { dashboard, explorer, search, export, logs, settings, help };
        CurrentPage = dashboard;
        WeakReferenceMessenger.Default.Register<MainViewModel, OpenFileMessage>(this, static (recipient, message) => recipient.OnOpenFile(message));
        _theme.ThemeChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ThemeGlyph));
            OnPropertyChanged(nameof(ThemeLabel));
            OnPropertyChanged(nameof(ThemeToolTip));
        };
    }

    public WorkspaceContext Context { get; }

    public ExportSelection Selection { get; }

    public ObservableCollection<PageViewModel> Pages { get; }

    public string WindowTitle => AppInfo.Title;

    public string VersionText => $"Versione {AppInfo.Version} · analisi locale, nessun dato inviato in rete";

    public string ShortVersionText => $"Versione {AppInfo.Version}";

    /// <summary>Icona del pulsante tema: sole nel tema scuro (passa a chiaro), luna nel tema chiaro.</summary>
    public string ThemeGlyph => _theme.IsDark ? "" : "";

    public string ThemeLabel => _theme.IsDark ? "Tema chiaro" : "Tema scuro";

    public string ThemeToolTip => _theme.FluentEnabled
        ? "Passa al tema " + (_theme.IsDark ? "chiaro" : "scuro") + " (Ctrl+Maiusc+T). Altre opzioni in Impostazioni."
        : "Il tema scuro richiede lo stile Fluent: attivalo in Impostazioni e riavvia l'applicazione.";

    public Task InitializeAsync() => Context.InitializeAsync();

    partial void OnCurrentPageChanged(PageViewModel value)
    {
        if (value is not null)
        {
            _ = value.OnActivatedAsync();
        }
    }

    /// <summary>Navigazione da tastiera: "1".."7" (posizione nel menu) oppure search, export, help.</summary>
    [RelayCommand]
    private void Navigate(string? target)
    {
        PageViewModel? page = target switch
        {
            "search" => _search,
            "export" => _export,
            "help" => _help,
            _ when int.TryParse(target, out var number) && number >= 1 && number <= Pages.Count => Pages[number - 1],
            _ => null,
        };

        if (page is not null)
        {
            CurrentPage = page;
        }
    }

    [RelayCommand]
    private void ShowHelp() => CurrentPage = _help;

    private bool CanToggleTheme() => _theme.FluentEnabled;

    [RelayCommand(CanExecute = nameof(CanToggleTheme))]
    private void ToggleTheme() => _theme.Toggle();

    private void OnOpenFile(OpenFileMessage message)
    {
        CurrentPage = _explorer;
        _ = _explorer.OpenFileAsync(message.RelativePath, message.Line);
    }
}
