using System.IO;
using System.Windows;
using System.Windows.Threading;
using Lcia.App.Services;
using Lcia.App.ViewModels;
using Lcia.Application;
using Lcia.Hosting;
using Lcia.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lcia.App;

/// <summary>
/// Avvio dell'applicazione: composizione dei servizi (DI), tema chiaro/scuro (Fluent),
/// gestione centralizzata degli errori non gestiti.
/// </summary>
public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;
    private ILogger<App>? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            var services = new ServiceCollection();
            services.AddLocalCodeIntelligence();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<ExportSelection>();
            services.AddSingleton<WorkspaceContext>();
            services.AddSingleton<DashboardViewModel>();
            services.AddSingleton<ExplorerViewModel>();
            services.AddSingleton<SearchViewModel>();
            services.AddSingleton<ExportViewModel>();
            services.AddSingleton<LogsViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<HelpViewModel>();
            services.AddSingleton<ThemeService>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();
            _services = services.BuildServiceProvider();
            _logger = _services.GetRequiredService<ILogger<App>>();

            // Il tema va applicato prima di creare la finestra, così i controlli nascono già con lo stile giusto.
            _services.GetRequiredService<ThemeService>().Initialize();

            var window = _services.GetRequiredService<MainWindow>();
            _services.GetRequiredService<ThemeService>().AttachWindow(window);
            MainWindow = window;
            window.Show();
            _logger.LogInformation("Applicazione avviata (versione {Version}).", AppInfo.Version);
        }
        catch (Exception ex)
        {
            ReportStartupFailure(ex);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("Applicazione chiusa.");
        _services?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Errore non gestito nell'interfaccia.");
        MessageBox.Show(
            "Si è verificato un errore imprevisto:\n\n" + e.Exception.Message + "\n\nL'operazione è stata interrotta; i dettagli sono nel log.",
            "Local Code Intelligence Analyzer",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _logger?.LogCritical(exception, "Errore fatale non gestito.");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Eccezione non osservata in un'attività in background.");
        e.SetObserved();
    }

    private static void ReportStartupFailure(Exception exception)
    {
        var message = "Avvio non riuscito: " + exception.Message;
        try
        {
            var paths = new AppPaths();
            Directory.CreateDirectory(paths.DataDirectory);
            File.WriteAllText(Path.Combine(paths.DataDirectory, "errore-avvio.log"), DateTimeOffset.Now + Environment.NewLine + exception);
            message += Environment.NewLine + Environment.NewLine + "Dettagli: " + Path.Combine(paths.DataDirectory, "errore-avvio.log");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nessuna cartella dati disponibile: si mostra solo il messaggio.
        }

        MessageBox.Show(message, "Local Code Intelligence Analyzer", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
