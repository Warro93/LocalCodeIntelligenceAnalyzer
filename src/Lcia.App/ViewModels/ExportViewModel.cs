using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lcia.App.Services;
using Lcia.Application;
using Microsoft.Extensions.Logging;

namespace Lcia.App.ViewModels;

/// <summary>Export Center: FULL PROJECT CONTEXT EXPORT con filtri, stima, suddivisione e oscuramento segreti.</summary>
public sealed partial class ExportViewModel : PageViewModel
{
    private readonly WorkspaceContext _context;
    private readonly IExportService _export;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly ILogger<ExportViewModel> _logger;
    private bool _unredactedConfirmed;
    private bool _sensitiveConfirmed;
    private bool _reverting;

    [ObservableProperty]
    private Option<ExportFormat> _selectedFormat;

    [ObservableProperty]
    private Option<ExportScope> _selectedScope;

    [ObservableProperty]
    private Option<int> _selectedPartSize;

    [ObservableProperty]
    private string _extensionsText = string.Empty;

    [ObservableProperty]
    private string _foldersText = string.Empty;

    [ObservableProperty]
    private string _excludedFoldersText = string.Empty;

    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    private string _baseFileName = string.Empty;

    [ObservableProperty]
    private bool _includeDirectoryTree = true;

    [ObservableProperty]
    private bool _includeFileList = true;

    [ObservableProperty]
    private bool _includeProjects = true;

    [ObservableProperty]
    private bool _includeSymbolIndex = true;

    [ObservableProperty]
    private bool _includeSourceCode = true;

    [ObservableProperty]
    private bool _includeLineNumbers = true;

    [ObservableProperty]
    private bool _includeAbsolutePaths;

    [ObservableProperty]
    private bool _redactSecrets = true;

    [ObservableProperty]
    private bool _includeSensitiveFiles;

    [ObservableProperty]
    private string _planText = "Premi 'Stima' per calcolare file, dimensione e token prima di esportare.";

    [ObservableProperty]
    private string _resultText = string.Empty;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string? _lastOutputDirectory;

    public ExportViewModel(
        WorkspaceContext context,
        IExportService export,
        ISettingsStore settings,
        ExportSelection selection,
        IDialogService dialogs,
        ILogger<ExportViewModel> logger)
        : base("Esporta", "", "Full Project Context Export per qualsiasi AI")
    {
        _context = context;
        _export = export;
        _settings = settings;
        _dialogs = dialogs;
        _logger = logger;
        Selection = selection;
        _selectedFormat = Formats[0];
        _selectedScope = Scopes[0];

        var stored = settings.Load();
        _outputDirectory = stored.LastExportDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LCIA-Exports");
        _selectedPartSize = PartSizes.FirstOrDefault(p => p.Value == stored.DefaultMaxPartTokens) ?? PartSizes[0];
    }

    public ExportSelection Selection { get; }

    public IReadOnlyList<Option<ExportFormat>> Formats { get; } =
    [
        new(ExportFormat.Text, "Testo (.txt)"),
        new(ExportFormat.Markdown, "Markdown (.md)"),
    ];

    public IReadOnlyList<Option<ExportScope>> Scopes { get; } =
    [
        new(ExportScope.Full, "FULL EXPORT - tutto il progetto (con i filtri sotto)"),
        new(ExportScope.Custom, "CUSTOM EXPORT - solo i file selezionati da Cerca/Esplora"),
    ];

    public IReadOnlyList<Option<int>> PartSizes { get; } =
    [
        new(0, "File unico (nessuna suddivisione)"),
        new(32_000, "Parti da ~32.000 token"),
        new(100_000, "Parti da ~100.000 token"),
        new(200_000, "Parti da ~200.000 token"),
        new(500_000, "Parti da ~500.000 token"),
        new(1_000_000, "Parti da ~1.000.000 token"),
    ];

    public ObservableCollection<string> WrittenFiles { get; } = [];

    partial void OnRedactSecretsChanged(bool value)
    {
        if (value || _reverting)
        {
            _unredactedConfirmed = value ? false : _unredactedConfirmed;
            return;
        }

        var confirmed = _dialogs.Confirm(
            "Disattivare l'oscuramento dei segreti?",
            "Il dossier potrebbe contenere password, token, chiavi private e connection string in chiaro.\n\n" +
            "Procedi solo se il file resterà sul tuo PC o verrà condiviso con un servizio di cui ti fidi. Confermi?");
        if (confirmed)
        {
            _unredactedConfirmed = true;
            return;
        }

        Revert(() => RedactSecrets = true);
    }

    partial void OnIncludeSensitiveFilesChanged(bool value)
    {
        if (!value || _reverting)
        {
            _sensitiveConfirmed = value && _sensitiveConfirmed;
            return;
        }

        var confirmed = _dialogs.Confirm(
            "Includere i file sensibili?",
            "Verranno inclusi file come secrets.json, .env e chiavi, normalmente esclusi.\n\nConfermi l'inclusione consapevole?");
        if (confirmed)
        {
            _sensitiveConfirmed = true;
            return;
        }

        Revert(() => IncludeSensitiveFiles = false);
    }

    [RelayCommand]
    private void BrowseOutput()
    {
        var folder = _dialogs.PickFolder("Cartella di destinazione dell'esportazione (esterna al repository)", OutputDirectory);
        if (folder is not null)
        {
            OutputDirectory = folder;
        }
    }

    [RelayCommand]
    private void ClearSelection() => Selection.Clear();

    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (!string.IsNullOrEmpty(LastOutputDirectory))
        {
            _dialogs.OpenFolder(LastOutputDirectory);
        }
    }

    [RelayCommand]
    private async Task EstimateAsync()
    {
        if (BuildRequest() is not { } request)
        {
            return;
        }

        try
        {
            var plan = await _export.PlanAsync(request);
            var builder = new StringBuilder();
            builder.AppendLine($"{plan.Files.Count:N0} file · ~{plan.EstimatedCharacters:N0} caratteri · ~{plan.EstimatedTokens:N0} token stimati · {plan.EstimatedParts} parte/i");
            foreach (var group in plan.Files.GroupBy(f => f.Language).OrderByDescending(g => g.Count()).Take(8))
            {
                builder.AppendLine($"  {Lcia.Domain.LanguageCatalog.Describe(group.Key).DisplayName}: {group.Count():N0} file");
            }

            foreach (var warning in plan.Warnings)
            {
                builder.AppendLine("Avviso: " + warning);
            }

            builder.Append("La stima usa ~4 caratteri per token: il valore reale dipende dal modello di AI.");
            PlanText = builder.ToString();
        }
        catch (Exception ex)
        {
            PlanText = "Stima non riuscita: " + ex.Message;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ExportAsync(CancellationToken cancellationToken)
    {
        if (BuildRequest() is not { } request)
        {
            return;
        }

        WrittenFiles.Clear();
        ResultText = "Esportazione in corso…";
        ProgressValue = 0;
        var progress = new Progress<ExportProgress>(p => ProgressValue = p.Total == 0 ? 0 : p.Processed * 100.0 / p.Total);
        try
        {
            var result = await Task.Run(() => _export.ExportAsync(request, progress, cancellationToken), cancellationToken);
            foreach (var file in result.WrittenFiles)
            {
                WrittenFiles.Add(file);
            }

            LastOutputDirectory = Path.GetFullPath(request.OutputDirectory);
            var builder = new StringBuilder();
            builder.AppendLine($"Esportati {result.FilesExported:N0} file in {result.Parts.Count} parte/i · {result.TotalCharacters:N0} caratteri · ~{result.EstimatedTokens:N0} token.");
            builder.AppendLine(request.RedactSecrets
                ? $"Valori oscurati: {result.Redactions.Count} (elencati in fondo al dossier, senza i valori)."
                : "ATTENZIONE: oscuramento dei segreti disattivato su richiesta esplicita.");
            if (result.ModifiedSinceIndex.Count > 0)
            {
                builder.AppendLine($"{result.ModifiedSinceIndex.Count} file modificati dopo l'ultima indicizzazione: aggiorna l'indice per metadati allineati.");
            }

            foreach (var warning in result.Warnings.Take(10))
            {
                builder.AppendLine("Avviso: " + warning);
            }

            ResultText = builder.ToString().TrimEnd();
            ProgressValue = 100;
            SaveDefaults(request);
        }
        catch (OperationCanceledException)
        {
            ResultText = "Esportazione annullata: i file parziali sono stati eliminati.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Esportazione non riuscita.");
            ResultText = "Esportazione non riuscita: " + ex.Message;
            _dialogs.ShowError("Esportazione non riuscita", ex.Message);
        }
    }

    private ExportRequest? BuildRequest()
    {
        var repository = _context.CurrentRepository;
        if (repository is null)
        {
            PlanText = ResultText = "Nessun repository selezionato.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            PlanText = ResultText = "Scegli la cartella di destinazione.";
            return null;
        }

        var scope = SelectedScope.Value;
        if (scope == ExportScope.Custom && (Selection.Count == 0 || Selection.RepositoryId != repository.Id))
        {
            PlanText = ResultText = "La selezione personalizzata è vuota per questo repository: aggiungi file da 'Cerca' o 'Esplora'.";
            return null;
        }

        return new ExportRequest
        {
            RepositoryId = repository.Id,
            OutputDirectory = OutputDirectory.Trim(),
            Format = SelectedFormat.Value,
            Scope = scope,
            SelectedFiles = scope == ExportScope.Custom ? Selection.Paths : [],
            IncludeExtensions = SplitList(ExtensionsText),
            IncludePathPrefixes = SplitList(FoldersText),
            ExcludePathPrefixes = SplitList(ExcludedFoldersText),
            IncludeDirectoryTree = IncludeDirectoryTree,
            IncludeFileList = IncludeFileList,
            IncludeProjects = IncludeProjects,
            IncludeSymbolIndex = IncludeSymbolIndex,
            IncludeSourceCode = IncludeSourceCode,
            IncludeLineNumbers = IncludeLineNumbers,
            IncludeAbsolutePaths = IncludeAbsolutePaths,
            RedactSecrets = RedactSecrets,
            UnredactedExportConfirmed = !RedactSecrets && _unredactedConfirmed,
            IncludeSensitiveFiles = IncludeSensitiveFiles,
            SensitiveFilesConfirmed = IncludeSensitiveFiles && _sensitiveConfirmed,
            MaxPartTokens = SelectedPartSize.Value,
            BaseFileName = string.IsNullOrWhiteSpace(BaseFileName) ? null : BaseFileName.Trim(),
        };
    }

    private void SaveDefaults(ExportRequest request)
    {
        try
        {
            var settings = _settings.Load();
            settings.LastExportDirectory = request.OutputDirectory;
            settings.DefaultMaxPartTokens = request.MaxPartTokens;
            _settings.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Impossibile salvare le preferenze di esportazione.");
        }
    }

    private void Revert(Action action)
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _reverting = true;
            try
            {
                action();
            }
            finally
            {
                _reverting = false;
            }
        });
    }

    private static string[] SplitList(string text) =>
        text.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
