using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lcia.App.Services;
using Lcia.Application;
using Lcia.Domain;
using Microsoft.Extensions.Logging;

namespace Lcia.App.ViewModels;

public sealed record LanguageRow(string Language, int Files, string Lines, string Size, string Support);

public sealed record ScanRunRow(string Started, string Status, string Duration, string Summary, string? Message);

public sealed record SupportRow(string Language, string Level, string Description);

/// <summary>Dashboard: repository corrente, statistiche, ultime scansioni, avvisi e livelli di supporto.</summary>
public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly WorkspaceContext _context;
    private readonly IIndexStore _store;
    private readonly ILogger<DashboardViewModel> _logger;
    private int _refreshVersion;

    [ObservableProperty]
    private string _repositoryTitle = "Nessun repository selezionato";

    [ObservableProperty]
    private string _repositoryDetails = "Usa 'Apri cartella' o 'Apri soluzione' per indicizzare un progetto locale.";

    [ObservableProperty]
    private string _filesText = "-";

    [ObservableProperty]
    private string _linesText = "-";

    [ObservableProperty]
    private string _symbolsText = "-";

    [ObservableProperty]
    private string _projectsText = "-";

    [ObservableProperty]
    private string _skippedText = string.Empty;

    [ObservableProperty]
    private string _warningsText = string.Empty;

    public DashboardViewModel(WorkspaceContext context, IIndexStore store, ILogger<DashboardViewModel> logger)
        : base("Dashboard", "", "Repository, stato della scansione e statistiche")
    {
        _context = context;
        _store = store;
        _logger = logger;
        _context.RepositoryChanged += (_, _) => _ = RefreshAsync();
        _context.IndexUpdated += (_, _) => _ = RefreshAsync();
        SupportMatrix = LanguageCatalog.All
            .Select(l => new SupportRow(l.DisplayName, SupportLabel(l.CurrentSupport), l.SupportDescription))
            .ToArray();
    }

    public WorkspaceContext Context => _context;

    public ObservableCollection<LanguageRow> Languages { get; } = [];

    public ObservableCollection<ScanRunRow> ScanRuns { get; } = [];

    public IReadOnlyList<SupportRow> SupportMatrix { get; }

    public override Task OnActivatedAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var version = ++_refreshVersion;
        var repository = _context.CurrentRepository;
        if (repository is null)
        {
            Languages.Clear();
            ScanRuns.Clear();
            RepositoryTitle = "Nessun repository selezionato";
            RepositoryDetails = "Usa 'Apri cartella' o 'Apri soluzione' per indicizzare un progetto locale. L'analisi avviene interamente sul tuo PC.";
            FilesText = LinesText = SymbolsText = ProjectsText = "-";
            SkippedText = WarningsText = string.Empty;
            return;
        }

        try
        {
            var stats = await _store.GetStatisticsAsync(repository.Id);
            var runs = await _store.ListScanRunsAsync(repository.Id, 10);
            if (version != _refreshVersion)
            {
                return;
            }

            Languages.Clear();
            ScanRuns.Clear();
            RepositoryTitle = repository.DisplayName;
            var git = repository.GitBranch is null && repository.GitCommit is null
                ? "Git: non rilevato"
                : $"Git: {repository.GitBranch ?? "(HEAD staccato)"} @ {Short(repository.GitCommit)}";
            var scanned = repository.LastScanUtc?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "mai";
            RepositoryDetails = $"{repository.RootPath}\n{git} · ultima indicizzazione: {scanned}";

            FilesText = stats.IndexedFiles.ToString("N0", CultureInfo.CurrentCulture);
            LinesText = stats.TotalLines.ToString("N0", CultureInfo.CurrentCulture);
            SymbolsText = $"{stats.Symbols.ToString("N0", CultureInfo.CurrentCulture)} simboli · {stats.Relations.ToString("N0", CultureInfo.CurrentCulture)} relazioni";
            ProjectsText = $"{stats.Projects} progetti · {stats.Solutions} soluzioni";
            SkippedText = stats.SkippedFiles == 0
                ? string.Empty
                : "File elencati ma non indicizzati: " + string.Join(", ", stats.ByStatus
                    .Where(s => s.Key != FileIndexStatus.Indexed && s.Value > 0)
                    .Select(s => $"{s.Value} {StatusLabel(s.Key)}"));

            foreach (var language in stats.Languages)
            {
                Languages.Add(new LanguageRow(
                    LanguageCatalog.Describe(language.Language).DisplayName,
                    language.Files,
                    language.Lines.ToString("N0", CultureInfo.CurrentCulture),
                    FormatBytes(language.Bytes),
                    SupportLabel(language.Support)));
            }

            foreach (var run in runs)
            {
                ScanRuns.Add(new ScanRunRow(
                    run.StartedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    StatusLabel(run.Status),
                    run.Duration is { } duration ? $"{duration.TotalSeconds:N1} s" : "-",
                    $"{run.FilesSeen} trovati · {run.FilesIndexed} analizzati · {run.FilesUnchanged} invariati · {run.FilesRemoved} rimossi · {run.WarningCount} avvisi",
                    run.Message));
            }

            WarningsText = runs.FirstOrDefault()?.WarningsText ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Aggiornamento della dashboard non riuscito.");
            RepositoryDetails = "Impossibile leggere le statistiche: " + ex.Message;
        }
    }

    internal static string SupportLabel(SupportLevel level) => level switch
    {
        SupportLevel.Semantic => "Semantico",
        SupportLevel.Syntactic => "Sintattico",
        SupportLevel.Structural => "Strutturale",
        _ => "Solo testo",
    };

    private static string StatusLabel(FileIndexStatus status) => status switch
    {
        FileIndexStatus.TooLarge => "troppo grandi",
        FileIndexStatus.Binary => "binari",
        FileIndexStatus.Unreadable => "non leggibili",
        _ => "indicizzati",
    };

    private static string StatusLabel(ScanRunStatus status) => status switch
    {
        ScanRunStatus.Completed => "Completata",
        ScanRunStatus.Cancelled => "Annullata / interrotta",
        ScanRunStatus.Failed => "Non riuscita",
        _ => "In corso",
    };

    private static string Short(string? commit) => commit is { Length: > 10 } c ? c[..10] : commit ?? "-";

    internal static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):N1} GB",
        >= 1024L * 1024 => $"{bytes / (1024.0 * 1024):N1} MB",
        >= 1024 => $"{bytes / 1024.0:N1} KB",
        _ => $"{bytes} B",
    };
}
