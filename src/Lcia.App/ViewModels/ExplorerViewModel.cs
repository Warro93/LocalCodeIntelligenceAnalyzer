using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lcia.App.Services;
using Lcia.Application;
using Lcia.Domain;
using Microsoft.Extensions.Logging;

namespace Lcia.App.ViewModels;

/// <summary>Nodo dell'albero file/cartelle.</summary>
public sealed partial class FileTreeNode : ObservableObject
{
    public FileTreeNode(string name, string relativePath, bool isDirectory, IndexedFile? file)
    {
        Name = name;
        RelativePath = relativePath;
        IsDirectory = isDirectory;
        File = file;
    }

    public string Name { get; }

    public string RelativePath { get; }

    public bool IsDirectory { get; }

    public IndexedFile? File { get; }

    public ObservableCollection<FileTreeNode> Children { get; } = [];

    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>Glifi Segoe Fluent Icons / MDL2: cartella e documento.</summary>
    public string Glyph => IsDirectory ? "" : "";

    public string Note => File is null || File.Status == FileIndexStatus.Indexed
        ? string.Empty
        : File.Status switch
        {
            FileIndexStatus.TooLarge => "(troppo grande)",
            FileIndexStatus.Binary => "(binario)",
            _ => "(non leggibile)",
        };

    public IEnumerable<string> DescendantFiles()
    {
        if (!IsDirectory)
        {
            yield return RelativePath;
            yield break;
        }

        foreach (var child in Children)
        {
            foreach (var path in child.DescendantFiles())
            {
                yield return path;
            }
        }
    }
}

public sealed record SymbolRow(string Kind, string Name, string Signature, int Line, int EndLine, int Depth, string? Documentation)
{
    public string Display => new string(' ', Depth * 2) + Name;

    public string Lines => Line == EndLine ? $"{Line}" : $"{Line}-{EndLine}";
}

/// <summary>Repository Explorer: albero dei file indicizzati, visualizzatore con numeri di riga e struttura dei simboli.</summary>
public sealed partial class ExplorerViewModel : PageViewModel
{
    private const long MaxViewerBytes = 32L * 1024 * 1024;

    private readonly WorkspaceContext _context;
    private readonly IIndexStore _store;
    private readonly ITextFileReader _reader;
    private readonly ExportSelection _selection;
    private readonly IDialogService _dialogs;
    private readonly ILogger<ExplorerViewModel> _logger;
    private IReadOnlyList<IndexedFile> _files = [];
    private long? _loadedRepositoryId;
    private long? _fileRepositoryId;

    [ObservableProperty]
    private string _treeFilter = string.Empty;

    [ObservableProperty]
    private FileTreeNode? _selectedNode;

    [ObservableProperty]
    private string _fileText = string.Empty;

    [ObservableProperty]
    private string? _currentFilePath;

    [ObservableProperty]
    private string _fileInfo = "Seleziona un file nell'albero.";

    [ObservableProperty]
    private bool _isModifiedSinceIndex;

    [ObservableProperty]
    private SymbolRow? _selectedSymbol;

    /// <summary>Riga richiesta (1-based) e contatore che la vista usa per rieseguire la navigazione.</summary>
    [ObservableProperty]
    private int _requestedLine;

    [ObservableProperty]
    private int _navigationVersion;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public ExplorerViewModel(
        WorkspaceContext context,
        IIndexStore store,
        ITextFileReader reader,
        ExportSelection selection,
        IDialogService dialogs,
        ILogger<ExplorerViewModel> logger)
        : base("Esplora", "", "Albero dei file, visualizzatore del codice e simboli")
    {
        _context = context;
        _store = store;
        _reader = reader;
        _selection = selection;
        _dialogs = dialogs;
        _logger = logger;
        _context.RepositoryChanged += (_, _) => _loadedRepositoryId = null;
        _context.IndexUpdated += (_, _) => _loadedRepositoryId = null;
    }

    public ObservableCollection<FileTreeNode> RootNodes { get; } = [];

    public ObservableCollection<SymbolRow> Symbols { get; } = [];

    public string FileExtension => Path.GetExtension(CurrentFilePath ?? string.Empty);

    public override async Task OnActivatedAsync()
    {
        var repository = _context.CurrentRepository;
        if (repository is null)
        {
            RootNodes.Clear();
            ClearCurrentFile();
            _loadedRepositoryId = null;
            StatusText = "Nessun repository selezionato: apri una cartella o una soluzione.";
            return;
        }

        if (_loadedRepositoryId == repository.Id)
        {
            return;
        }

        // Repository diverso: il file mostrato apparteneva al precedente (dopo un aggiornamento dell'indice resta).
        if (_fileRepositoryId != repository.Id)
        {
            ClearCurrentFile();
        }

        try
        {
            _files = await _store.ListFilesAsync(repository.Id);
            _loadedRepositoryId = repository.Id;
            BuildTree();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Caricamento dell'albero non riuscito.");
            StatusText = "Impossibile caricare l'elenco dei file: " + ex.Message;
        }
    }

    partial void OnTreeFilterChanged(string value) => BuildTree();

    private void ClearCurrentFile()
    {
        _fileRepositoryId = null;
        CurrentFilePath = null;
        FileText = string.Empty;
        FileInfo = "Seleziona un file nell'albero.";
        IsModifiedSinceIndex = false;
        Symbols.Clear();
    }

    partial void OnSelectedNodeChanged(FileTreeNode? value)
    {
        if (value is { IsDirectory: false })
        {
            _ = OpenFileAsync(value.RelativePath, 0);
        }
    }

    partial void OnSelectedSymbolChanged(SymbolRow? value)
    {
        if (value is not null)
        {
            NavigateTo(value.Line);
        }
    }

    partial void OnCurrentFilePathChanged(string? value) => OnPropertyChanged(nameof(FileExtension));

    public async Task OpenFileAsync(string relativePath, int line)
    {
        var repository = _context.CurrentRepository;
        if (repository is null)
        {
            return;
        }

        try
        {
            if (_loadedRepositoryId != repository.Id)
            {
                await OnActivatedAsync();
            }

            var file = await _store.GetFileAsync(repository.Id, relativePath);
            var fullPath = PathUtilities.ResolveInside(repository.RootPath, relativePath);
            var read = await Task.Run(() => _reader.Read(fullPath, MaxViewerBytes, CancellationToken.None));
            if (read.Status != TextReadStatus.Ok || read.Text is null)
            {
                FileText = string.Empty;
                CurrentFilePath = relativePath;
                _fileRepositoryId = repository.Id;
                Symbols.Clear();
                FileInfo = $"{relativePath}: impossibile visualizzare il file ({read.Error ?? read.Status.ToString()}).";
                return;
            }

            CurrentFilePath = relativePath;
            _fileRepositoryId = repository.Id;
            FileText = read.Text;
            IsModifiedSinceIndex = file?.Sha256 is not null && !string.Equals(file.Sha256, read.Sha256, StringComparison.OrdinalIgnoreCase);
            var language = LanguageCatalog.Describe(file?.Language ?? LanguageCatalog.FromPath(relativePath));
            FileInfo = $"{relativePath} · {language.DisplayName} · {read.LineCount:N0} righe · {DashboardViewModel.FormatBytes(read.SizeBytes)} · " +
                       $"{read.EncodingName} · analisi: {DashboardViewModel.SupportLabel(file?.AnalysisLevel ?? SupportLevel.TextOnly)}";

            await LoadSymbolsAsync(file);
            NavigateTo(line > 0 ? line : 1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Apertura del file non riuscita: {Path}", relativePath);
            FileInfo = $"Impossibile aprire {relativePath}: {ex.Message}";
        }
    }

    private async Task LoadSymbolsAsync(IndexedFile? file)
    {
        Symbols.Clear();
        if (file is null)
        {
            return;
        }

        var symbols = await _store.ListSymbolsAsync(file.Id);
        var depthById = new Dictionary<long, int>();
        foreach (var symbol in symbols.Where(s => s.Kind != SymbolKind.Namespace))
        {
            var depth = symbol.ParentId is long parent && depthById.TryGetValue(parent, out var parentDepth) ? parentDepth + 1 : 0;
            depthById[symbol.Id] = depth;
            Symbols.Add(new SymbolRow(symbol.Kind.ToItalian(), symbol.Name, symbol.Signature ?? symbol.Name, symbol.StartLine, symbol.EndLine, depth, symbol.Documentation));
        }
    }

    private void NavigateTo(int line)
    {
        RequestedLine = Math.Max(1, line);
        NavigationVersion++;
    }

    [RelayCommand]
    private void AddSelectionToExport()
    {
        var repository = _context.CurrentRepository;
        if (repository is null || SelectedNode is null)
        {
            StatusText = "Seleziona un file o una cartella.";
            return;
        }

        var added = _selection.Add(repository.Id, SelectedNode.DescendantFiles());
        StatusText = $"{added} file aggiunti alla selezione di esportazione (totale {_selection.Count}).";
    }

    [RelayCommand]
    private void RevealInExplorer()
    {
        var repository = _context.CurrentRepository;
        if (repository is null || CurrentFilePath is null)
        {
            return;
        }

        _dialogs.RevealFile(PathUtilities.ResolveInside(repository.RootPath, CurrentFilePath));
    }

    [RelayCommand]
    private void CopyPath()
    {
        if (CurrentFilePath is not null && _dialogs.TrySetClipboard(CurrentFilePath))
        {
            StatusText = "Percorso copiato negli appunti.";
        }
    }

    [RelayCommand]
    private void ExpandAll() => SetExpanded(RootNodes, true);

    [RelayCommand]
    private void CollapseAll() => SetExpanded(RootNodes, false);

    private static void SetExpanded(IEnumerable<FileTreeNode> nodes, bool expanded)
    {
        foreach (var node in nodes.Where(n => n.IsDirectory))
        {
            node.IsExpanded = expanded;
            SetExpanded(node.Children, expanded);
        }
    }

    private void BuildTree()
    {
        RootNodes.Clear();
        var filter = TreeFilter.Trim();
        var files = filter.Length == 0
            ? _files
            : _files.Where(f => f.RelativePath.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        var root = new FileTreeNode(string.Empty, string.Empty, true, null);
        var directories = new Dictionary<string, FileTreeNode>(StringComparer.OrdinalIgnoreCase) { [string.Empty] = root };
        foreach (var file in files)
        {
            var parts = file.RelativePath.Split('/');
            var parent = root;
            var current = string.Empty;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                current = current.Length == 0 ? parts[i] : current + "/" + parts[i];
                if (!directories.TryGetValue(current, out var directory))
                {
                    directory = new FileTreeNode(parts[i], current, true, null) { IsExpanded = filter.Length > 0 };
                    parent.Children.Add(directory);
                    directories[current] = directory;
                }

                parent = directory;
            }

            parent.Children.Add(new FileTreeNode(parts[^1], file.RelativePath, false, file));
        }

        Sort(root);
        foreach (var node in root.Children)
        {
            RootNodes.Add(node);
        }

        StatusText = filter.Length == 0
            ? $"{_files.Count:N0} file nell'indice."
            : $"{files.Count:N0} file su {_files.Count:N0} corrispondono al filtro.";
    }

    private static void Sort(FileTreeNode node)
    {
        if (node.Children.Count == 0)
        {
            return;
        }

        var ordered = node.Children
            .OrderBy(c => c.IsDirectory ? 0 : 1)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        node.Children.Clear();
        foreach (var child in ordered)
        {
            Sort(child);
            node.Children.Add(child);
        }
    }
}
