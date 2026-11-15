using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lcia.App.Services;
using Lcia.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Lcia.App.ViewModels;

public sealed record LogRow(string Time, string Level, string Category, string Message);

/// <summary>Log consultabili: voci recenti in memoria (già oscurate) e accesso alla cartella dei file di log.</summary>
public sealed partial class LogsViewModel : PageViewModel
{
    private const int MaxRows = 2000;
    private readonly InMemoryLogSink _sink;
    private readonly AppPaths _paths;
    private readonly IDialogService _dialogs;
    private readonly DispatcherTimer _timer;
    private long _lastSequence;

    [ObservableProperty]
    private Option<LogLevel> _minimumLevel;

    public LogsViewModel(InMemoryLogSink sink, AppPaths paths, IDialogService dialogs)
        : base("Log", "", "Diagnostica dell'applicazione (senza credenziali)")
    {
        _sink = sink;
        _paths = paths;
        _dialogs = dialogs;
        _minimumLevel = Levels[0];
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    public IReadOnlyList<Option<LogLevel>> Levels { get; } =
    [
        new(LogLevel.Information, "Informazioni e superiori"),
        new(LogLevel.Warning, "Avvisi ed errori"),
        new(LogLevel.Error, "Solo errori"),
    ];

    public ObservableCollection<LogRow> Rows { get; } = [];

    public string LogsDirectory => _paths.LogsDirectory;

    partial void OnMinimumLevelChanged(Option<LogLevel> value)
    {
        Rows.Clear();
        _lastSequence = 0;
        Poll();
    }

    [RelayCommand]
    private void OpenLogsFolder() => _dialogs.OpenFolder(_paths.LogsDirectory);

    [RelayCommand]
    private void ClearView()
    {
        Rows.Clear();
        _lastSequence = _sink.LastSequence;
    }

    private void Poll()
    {
        if (_sink.LastSequence == _lastSequence)
        {
            return;
        }

        foreach (var entry in _sink.GetEntriesAfter(_lastSequence))
        {
            _lastSequence = entry.Sequence;
            if (entry.Level < MinimumLevel.Value)
            {
                continue;
            }

            Rows.Insert(0, new LogRow(
                entry.Timestamp.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
                entry.Level.ToString(),
                entry.Category,
                entry.Message));
        }

        while (Rows.Count > MaxRows)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }
    }
}
