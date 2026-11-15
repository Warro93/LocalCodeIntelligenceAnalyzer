using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lcia.Application;
using Lcia.Domain;
using Microsoft.Extensions.Logging;

namespace Lcia.App.Services;

/// <summary>
/// Stato condiviso dell'applicazione: repository corrente, elenco repository indicizzati,
/// operazione di indicizzazione in corso (progresso, annullamento) e barra di stato.
/// Tutto il lavoro pesante gira fuori dal thread UI.
/// </summary>
public sealed partial class WorkspaceContext : ObservableObject
{
    private readonly IndexingService _indexing;
    private readonly IIndexStore _store;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly ILogger<WorkspaceContext> _logger;
    private CancellationTokenSource? _cancellation;

    public WorkspaceContext(IndexingService indexing, IIndexStore store, ISettingsStore settings, IDialogService dialogs, ILogger<WorkspaceContext> logger)
    {
        _indexing = indexing;
        _store = store;
        _settings = settings;
        _dialogs = dialogs;
        _logger = logger;
    }

    /// <summary>Repository attivo cambiato (selezione o nuovo indice).</summary>
    public event EventHandler? RepositoryChanged;

    /// <summary>L'indice del repository corrente è stato aggiornato o eliminato.</summary>
    public event EventHandler? IndexUpdated;

    public ObservableCollection<RepositoryInfo> Repositories { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRepository))]
    [NotifyCanExecuteChangedFor(nameof(ReindexCommand), nameof(FullReindexCommand), nameof(RemoveIndexCommand))]
    private RepositoryInfo? _currentRepository;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand), nameof(OpenSolutionCommand), nameof(ReindexCommand),
        nameof(FullReindexCommand), nameof(RemoveIndexCommand), nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    [ObservableProperty]
    private string _statusText = "Pronto. Apri una cartella o una soluzione per iniziare.";

    [ObservableProperty]
    private IReadOnlyList<ScanWarning> _lastWarnings = [];

    public bool HasRepository => CurrentRepository is not null;

    public bool IsNotBusy => !IsBusy;

    public async Task InitializeAsync()
    {
        try
        {
            await _store.InitializeAsync();
            var interrupted = await _store.MarkInterruptedScanRunsAsync();
            if (interrupted > 0)
            {
                StatusText = $"{interrupted} indicizzazione/i interrotta/e in precedenza: riesegui 'Aggiorna indice' per riprendere.";
            }

            await RefreshRepositoriesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Inizializzazione dell'indice non riuscita.");
            StatusText = "Indice locale non disponibile: " + ex.Message;
            _dialogs.ShowError("Indice locale non disponibile", ex.Message);
        }
    }

    public async Task RefreshRepositoriesAsync(long? selectId = null)
    {
        var keep = selectId ?? CurrentRepository?.Id;
        var list = await _store.ListRepositoriesAsync();
        Repositories.Clear();
        foreach (var repository in list)
        {
            Repositories.Add(repository);
        }

        CurrentRepository = Repositories.FirstOrDefault(r => r.Id == keep) ?? Repositories.FirstOrDefault();
    }

    partial void OnCurrentRepositoryChanged(RepositoryInfo? value) => RepositoryChanged?.Invoke(this, EventArgs.Empty);

    private bool CanStart() => !IsBusy;

    private bool CanUseRepository() => !IsBusy && CurrentRepository is not null;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task OpenFolderAsync()
    {
        var path = _dialogs.PickFolder("Seleziona la cartella del progetto o del repository Git", _settings.Load().RecentPaths.FirstOrDefault());
        if (path is not null)
        {
            await IndexPathAsync(path, force: false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task OpenSolutionAsync()
    {
        var path = _dialogs.PickFile("Seleziona una soluzione Visual Studio", "Soluzioni Visual Studio (*.sln;*.slnx)|*.sln;*.slnx");
        if (path is not null)
        {
            await IndexPathAsync(path, force: false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseRepository))]
    private Task ReindexAsync() => IndexPathAsync(CurrentRepository!.EntryPath ?? CurrentRepository.RootPath, force: false);

    [RelayCommand(CanExecute = nameof(CanUseRepository))]
    private Task FullReindexAsync() => IndexPathAsync(CurrentRepository!.EntryPath ?? CurrentRepository.RootPath, force: true);

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cancellation?.Cancel();
        StatusText = "Annullamento in corso…";
    }

    [RelayCommand(CanExecute = nameof(CanUseRepository))]
    private async Task RemoveIndexAsync()
    {
        var repository = CurrentRepository!;
        if (!_dialogs.Confirm("Elimina indice",
                $"Eliminare l'indice locale di '{repository.Name}'?\n\nI file sorgente NON verranno toccati: viene rimosso solo il database dell'applicazione."))
        {
            return;
        }

        try
        {
            IsBusy = true;
            await _store.DeleteRepositoryAsync(repository.Id);
            StatusText = $"Indice di '{repository.Name}' eliminato. I file sorgente non sono stati modificati.";
            CurrentRepository = null;
            await RefreshRepositoriesAsync();
            IndexUpdated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eliminazione dell'indice non riuscita.");
            _dialogs.ShowError("Eliminazione non riuscita", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task IndexPathAsync(string path, bool force)
    {
        if (IsBusy)
        {
            return;
        }

        var settings = _settings.Load();
        var options = settings.ToScanOptions() with { ForceReindex = force };
        _cancellation = new CancellationTokenSource();
        IsBusy = true;
        IsProgressIndeterminate = true;
        ProgressValue = 0;
        StatusText = "Indicizzazione in corso…";
        var progress = new Progress<IndexingProgress>(OnProgress);
        var token = _cancellation.Token;

        try
        {
            var result = await Task.Run(() => _indexing.IndexAsync(new IndexingRequest(path, options), progress, token));
            LastWarnings = result.Warnings;
            StatusText = result.Run.Status switch
            {
                ScanRunStatus.Completed =>
                    $"Indicizzazione completata in {result.Elapsed.TotalSeconds:N1} s: {result.Run.FilesIndexed} file analizzati, " +
                    $"{result.Run.FilesUnchanged} invariati, {result.Run.FilesRemoved} rimossi, {result.Warnings.Count} avvisi.",
                ScanRunStatus.Cancelled => "Indicizzazione annullata: i file già elaborati restano nell'indice e verranno saltati alla prossima esecuzione.",
                _ => "Indicizzazione non riuscita: " + result.Run.Message,
            };

            settings.AddRecentPath(path);
            TrySaveSettings(settings);
            await RefreshRepositoriesAsync(result.Repository.Id);
            IndexUpdated?.Invoke(this, EventArgs.Empty);

            if (result.Run.Status == ScanRunStatus.Failed)
            {
                _dialogs.ShowError("Indicizzazione non riuscita", result.Run.Message ?? "Errore sconosciuto: vedi la schermata Log.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Indicizzazione non riuscita: {Path}", path);
            StatusText = "Errore: " + ex.Message;
            _dialogs.ShowError("Indicizzazione non riuscita", ex.Message);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Indicizzazione annullata.";
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
            ProgressValue = 0;
            _cancellation.Dispose();
            _cancellation = null;
        }
    }

    private void OnProgress(IndexingProgress progress)
    {
        StatusText = progress.Message;
        if (progress.Phase == IndexingPhase.Analyzing && progress.Total > 0)
        {
            IsProgressIndeterminate = false;
            ProgressValue = progress.Percent;
        }
        else if (progress.Phase != IndexingPhase.Completed)
        {
            IsProgressIndeterminate = true;
        }
    }

    private void TrySaveSettings(AppSettings settings)
    {
        try
        {
            _settings.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Impossibile aggiornare l'elenco dei percorsi recenti.");
        }
    }
}
