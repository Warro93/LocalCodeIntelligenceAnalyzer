using System.Collections;
using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Lcia.App.Services;
using Lcia.Application;
using Lcia.Domain;
using Microsoft.Extensions.Logging;

namespace Lcia.App.ViewModels;

public sealed record Option<T>(T Value, string Label);

public sealed record SearchHitRow(string RelativePath, int Line, string Preview, string Kind, string? Detail, string? Project);

/// <summary>Ricerca con filtri combinabili; i risultati possono essere aperti o aggiunti alla selezione di esportazione.</summary>
public sealed partial class SearchViewModel : PageViewModel
{
    private readonly WorkspaceContext _context;
    private readonly ISearchService _search;
    private readonly IIndexStore _store;
    private readonly ExportSelection _selection;
    private readonly IDialogService _dialogs;
    private readonly ILogger<SearchViewModel> _logger;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _queryText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSymbolMode), nameof(IsReferenceMode), nameof(ModeHelp))]
    private Option<SearchMode> _selectedMode;

    [ObservableProperty]
    private bool _caseSensitive;

    [ObservableProperty]
    private string _extensionsText = string.Empty;

    [ObservableProperty]
    private string _pathFilter = string.Empty;

    [ObservableProperty]
    private string _projectFilter = string.Empty;

    [ObservableProperty]
    private Option<SymbolKind?> _selectedSymbolKind;

    [ObservableProperty]
    private Option<RelationKind?> _selectedRelationKind;

    [ObservableProperty]
    private int _maxResults = 500;

    [ObservableProperty]
    private string _summary = "Scegli il repository, inserisci il testo e premi Cerca.";

    [ObservableProperty]
    private string? _warning;

    public SearchViewModel(
        WorkspaceContext context,
        ISearchService search,
        IIndexStore store,
        ExportSelection selection,
        IDialogService dialogs,
        ILogger<SearchViewModel> logger)
        : base("Cerca", "", "Testo, regex, simboli e riferimenti con filtri combinabili")
    {
        _context = context;
        _search = search;
        _store = store;
        _selection = selection;
        _dialogs = dialogs;
        _logger = logger;
        _selectedMode = Modes[0];
        _selectedSymbolKind = SymbolKinds[0];
        _selectedRelationKind = RelationKinds[0];
        _context.RepositoryChanged += (_, _) => _ = LoadProjectsAsync();
    }

    public IReadOnlyList<Option<SearchMode>> Modes { get; } =
    [
        new(SearchMode.Words, "Parole chiave (full-text)"),
        new(SearchMode.ExactText, "Testo esatto"),
        new(SearchMode.Regex, "Espressione regolare"),
        new(SearchMode.Symbol, "Simboli (classi, metodi, ...)"),
        new(SearchMode.References, "Riferimenti a tipo/membro"),
    ];

    public IReadOnlyList<Option<SymbolKind?>> SymbolKinds { get; } =
        new[] { new Option<SymbolKind?>(null, "Tutti i tipi di simbolo") }
            .Concat(Enum.GetValues<SymbolKind>().Select(k => new Option<SymbolKind?>(k, k.ToItalian())))
            .ToArray();

    public IReadOnlyList<Option<RelationKind?>> RelationKinds { get; } =
        new[] { new Option<RelationKind?>(null, "Tutte le relazioni") }
            .Concat(Enum.GetValues<RelationKind>().Select(k => new Option<RelationKind?>(k, k.ToItalian())))
            .ToArray();

    public ObservableCollection<string> Projects { get; } = [];

    public ObservableCollection<SearchHitRow> Results { get; } = [];

    public bool IsSymbolMode => SelectedMode.Value == SearchMode.Symbol;

    public bool IsReferenceMode => SelectedMode.Value == SearchMode.References;

    public string ModeHelp => SelectedMode.Value switch
    {
        SearchMode.Words => "Parole in AND; \"frase esatta\"; prefisso*; -esclusione. Trova anche identificatori composti (es. 'order' trova CreateOrderAsync).",
        SearchMode.ExactText => "Sottostringa esatta, riga per riga. Attiva 'Maiuscole/minuscole' per una corrispondenza sensibile.",
        SearchMode.Regex => "Espressione regolare .NET applicata a ogni riga (timeout di sicurezza di 1 secondo per riga).",
        SearchMode.Symbol => "Nome o nome completo del simbolo; * come jolly (es. *Repository). Filtra per tipo di simbolo.",
        _ => "Chi usa un tipo o un membro: tipi base, dipendenze, registrazioni DI, invocazioni, creazioni con new (analisi sintattica).",
    };

    public override Task OnActivatedAsync() => LoadProjectsAsync();

    private async Task LoadProjectsAsync()
    {
        Projects.Clear();
        Projects.Add(string.Empty);
        var repository = _context.CurrentRepository;
        if (repository is null)
        {
            return;
        }

        try
        {
            foreach (var project in (await _store.ListProjectsAsync(repository.Id)).Where(p => p.Kind == ProjectKind.Project).Select(p => p.Name).Distinct().Order())
            {
                Projects.Add(project);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Elenco progetti non disponibile.");
        }
    }

    private bool CanSearch() => !string.IsNullOrWhiteSpace(QueryText);

    [RelayCommand(CanExecute = nameof(CanSearch), IncludeCancelCommand = true)]
    private async Task SearchAsync(CancellationToken cancellationToken)
    {
        var repository = _context.CurrentRepository;
        if (repository is null)
        {
            Summary = "Nessun repository selezionato: apri o seleziona un repository indicizzato.";
            return;
        }

        var query = new SearchQuery
        {
            RepositoryId = repository.Id,
            Text = QueryText.Trim(),
            Mode = SelectedMode.Value,
            CaseSensitive = CaseSensitive,
            Extensions = ExtensionsText.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            PathContains = string.IsNullOrWhiteSpace(PathFilter) ? null : PathFilter.Trim(),
            ProjectName = string.IsNullOrWhiteSpace(ProjectFilter) ? null : ProjectFilter.Trim(),
            SymbolKinds = SelectedSymbolKind.Value is { } symbolKind ? new[] { symbolKind } : Array.Empty<SymbolKind>(),
            RelationKinds = SelectedRelationKind.Value is { } relationKind ? new[] { relationKind } : Array.Empty<RelationKind>(),
            MaxResults = Math.Clamp(MaxResults, 1, 20000),
        };

        Summary = "Ricerca in corso…";
        Warning = null;
        try
        {
            var result = await _search.SearchAsync(query, cancellationToken);
            Results.Clear();
            foreach (var hit in result.Hits)
            {
                Results.Add(new SearchHitRow(hit.RelativePath, hit.Line, hit.Preview, KindLabel(hit.Kind), hit.Detail, hit.ProjectName));
            }

            var files = result.Hits.Select(h => h.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            Summary = $"{result.Hits.Count:N0} risultati in {files:N0} file ({result.Elapsed.TotalMilliseconds:N0} ms)" +
                      (result.Truncated ? " — risultati troncati: aumenta il limite o restringi i filtri." : ".");
            Warning = result.Warning;
        }
        catch (OperationCanceledException)
        {
            Summary = "Ricerca annullata.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ricerca non riuscita.");
            Summary = "Ricerca non riuscita: " + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenHit(SearchHitRow? row)
    {
        if (row is not null)
        {
            WeakReferenceMessenger.Default.Send(new OpenFileMessage(row.RelativePath, row.Line));
        }
    }

    [RelayCommand]
    private void AddToExport(IList? selectedItems)
    {
        var repository = _context.CurrentRepository;
        if (repository is null)
        {
            return;
        }

        var rows = selectedItems is { Count: > 0 } ? selectedItems.OfType<SearchHitRow>() : Results;
        var added = _selection.Add(repository.Id, rows.Select(r => r.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase));
        Summary = $"{added} file aggiunti alla selezione di esportazione (totale {_selection.Count}). Vai su 'Esporta' e scegli 'Selezione personalizzata'.";
    }

    [RelayCommand]
    private void CopyResults()
    {
        if (Results.Count == 0)
        {
            return;
        }

        var builder = new StringBuilder();
        foreach (var row in Results)
        {
            builder.Append(row.RelativePath).Append(':').Append(row.Line).Append(": ").Append(row.Preview);
            if (!string.IsNullOrEmpty(row.Detail))
            {
                builder.Append("  [").Append(row.Detail).Append(']');
            }

            builder.AppendLine();
        }

        Summary = _dialogs.TrySetClipboard(builder.ToString())
            ? $"{Results.Count} risultati copiati negli appunti (percorso:riga: testo)."
            : "Impossibile accedere agli appunti: riprova.";
    }

    private static string KindLabel(SearchHitKind kind) => kind switch
    {
        SearchHitKind.Symbol => "Simbolo",
        SearchHitKind.Reference => "Riferimento",
        _ => "Testo",
    };
}
