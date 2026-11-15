using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lcia.App.Services;
using Lcia.Application;
using Lcia.Infrastructure;

namespace Lcia.App.ViewModels;

/// <summary>Argomento della guida: intervallo di blocchi [StartBlock, EndBlock) del manuale.</summary>
public sealed record HelpTopic(string Title, int Level, int StartBlock, int EndBlock, string SearchText)
{
    /// <summary>Rientro nell'indice per i sotto-argomenti.</summary>
    public double Indent => Level >= 3 ? 16 : 0;
}

/// <summary>Una scorciatoia da tastiera mostrata nella guida.</summary>
public sealed record ShortcutRow(string Keys, string Action);

/// <summary>
/// Guida in linea: mostra il manuale (docs/USER_GUIDE.md incorporato nell'eseguibile) diviso per argomenti,
/// con ricerca, scorciatoie da tastiera e informazioni sull'applicazione. Nessun accesso alla rete.
/// </summary>
public sealed partial class HelpViewModel : PageViewModel
{
    public const string ManualResourceName = "Lcia.App.Help.USER_GUIDE.md";

    private readonly AppPaths _paths;
    private readonly IDialogService _dialogs;
    private readonly IReadOnlyList<HelpTopic> _allTopics;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private HelpTopic? _selectedTopic;

    [ObservableProperty]
    private IReadOnlyList<MarkdownBlock> _currentBlocks = [];

    [ObservableProperty]
    private IReadOnlyList<string> _highlightTerms = [];

    [ObservableProperty]
    private string _resultText = string.Empty;

    public HelpViewModel(AppPaths paths, IDialogService dialogs)
        : base("Guida", "", "Manuale d'uso, scorciatoie da tastiera e informazioni (F1)")
    {
        _paths = paths;
        _dialogs = dialogs;
        Blocks = MarkdownLite.Parse(LoadManual());
        _allTopics = BuildTopics(Blocks);
        Topics = new ObservableCollection<HelpTopic>(_allTopics);
        SelectedTopic = Topics.FirstOrDefault();
    }

    public IReadOnlyList<MarkdownBlock> Blocks { get; }

    public ObservableCollection<HelpTopic> Topics { get; }

    public IReadOnlyList<ShortcutRow> Shortcuts { get; } =
    [
        new("F1", "Apri questa guida"),
        new("Ctrl+O", "Apri cartella"),
        new("Ctrl+Maiusc+O", "Apri soluzione (.sln / .slnx)"),
        new("F5", "Aggiorna indice (solo file cambiati)"),
        new("Ctrl+F", "Vai a Cerca"),
        new("Ctrl+E", "Vai a Esporta"),
        new("Ctrl+1 … Ctrl+7", "Dashboard, Esplora, Cerca, Esporta, Log, Impostazioni, Guida"),
        new("Ctrl+Maiusc+T", "Tema chiaro / scuro"),
        new("Invio (in Cerca)", "Avvia la ricerca"),
        new("Doppio clic su un risultato", "Apre il file alla riga trovata"),
    ];

    public string ProductText => $"{AppInfo.ProductName} {AppInfo.Version}";

    public string AboutText =>
        "Applicazione gratuita e interamente locale: nessun upload del codice, nessuna telemetria, " +
        "nessuna esecuzione del codice analizzato. I repository vengono aperti in sola lettura.";

    public string DataDirectory => _paths.DataDirectory;

    public string LogsDirectory => _paths.LogsDirectory;

    [RelayCommand]
    private void ShowWholeManual()
    {
        SearchText = string.Empty;
        SelectedTopic = Topics.FirstOrDefault();
    }

    [RelayCommand]
    private void OpenDataFolder() => _dialogs.OpenFolder(_paths.DataDirectory);

    [RelayCommand]
    private void OpenLogsFolder() => _dialogs.OpenFolder(_paths.LogsDirectory);

    partial void OnSearchTextChanged(string value)
    {
        var terms = SplitTerms(value);
        HighlightTerms = terms;
        Topics.Clear();
        var matches = terms.Count == 0
            ? _allTopics
            : _allTopics.Where(t => t.StartBlock >= 0 && t.Level > 1 && terms.All(term => t.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var topic in matches)
        {
            Topics.Add(topic);
        }

        ResultText = terms.Count == 0
            ? string.Empty
            : matches.Count == 0 ? "Nessun argomento contiene il testo cercato." : $"{matches.Count} argomenti trovati.";
        if (matches.Count == 0)
        {
            SelectedTopic = null;
            CurrentBlocks = [new MarkdownBlock(MarkdownBlockKind.Paragraph) { Text = $"Nessun argomento della guida contiene \"{value.Trim()}\". Prova con un'altra parola." }];
            return;
        }

        SelectedTopic = Topics[0];
    }

    partial void OnSelectedTopicChanged(HelpTopic? value)
    {
        if (value is null)
        {
            return;
        }

        var start = Math.Clamp(value.StartBlock, 0, Blocks.Count);
        var end = Math.Clamp(value.EndBlock, start, Blocks.Count);
        CurrentBlocks = Blocks.Skip(start).Take(end - start).ToArray();
    }

    /// <summary>Indice della guida: manuale completo, poi titoli di livello 2 e 3 con il testo della loro sezione.</summary>
    internal static List<HelpTopic> BuildTopics(IReadOnlyList<MarkdownBlock> blocks)
    {
        var topics = new List<HelpTopic>
        {
            new("Manuale completo", 1, 0, blocks.Count, string.Join(' ', blocks.Select(b => b.PlainText))),
        };

        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (block.Kind != MarkdownBlockKind.Heading || block.Level is < 2 or > 3)
            {
                continue;
            }

            var end = i + 1;
            while (end < blocks.Count && !(blocks[end].Kind == MarkdownBlockKind.Heading && blocks[end].Level <= block.Level))
            {
                end++;
            }

            var text = string.Join(' ', blocks.Skip(i).Take(end - i).Select(b => b.PlainText));
            topics.Add(new HelpTopic(MarkdownLite.ToPlainText(block.Text), block.Level, i, end, text));
        }

        return topics;
    }

    private static List<string> SplitTerms(string? text) =>
        (text ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string LoadManual()
    {
        using var stream = typeof(HelpViewModel).Assembly.GetManifestResourceStream(ManualResourceName);
        if (stream is null)
        {
            return "# Guida\n\nIl manuale non è incluso in questa versione: consulta docs/USER_GUIDE.md nella cartella del progetto.";
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
