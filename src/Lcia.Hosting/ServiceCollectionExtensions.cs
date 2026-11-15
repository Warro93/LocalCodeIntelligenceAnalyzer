using Lcia.Analysis.CSharp;
using Lcia.Application;
using Lcia.Export;
using Lcia.Infrastructure;
using Lcia.Scanner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lcia.Hosting;

public sealed class LciaHostOptions
{
    /// <summary>Cartella dati (indice, log, impostazioni). Null = predefinita o variabile LCIA_DATA_DIR.</summary>
    public string? DataDirectory { get; set; }

    public bool EnableFileLogging { get; set; } = true;

    public LogLevel MinimumLogLevel { get; set; } = LogLevel.Information;
}

public static class ServiceCollectionExtensions
{
    /// <summary>Registra tutti i motori (scanner, analisi, indice, ricerca, export) con dependency injection.</summary>
    public static IServiceCollection AddLocalCodeIntelligence(this IServiceCollection services, Action<LciaHostOptions>? configure = null)
    {
        var options = new LciaHostOptions();
        configure?.Invoke(options);

        var paths = new AppPaths(options.DataDirectory);
        paths.EnsureCreated();
        var secretScanner = new RegexSecretScanner();
        var memorySink = new InMemoryLogSink(2000, secretScanner.RedactLine) { MinimumLevel = options.MinimumLogLevel };

        services.AddSingleton(paths);
        services.AddSingleton(memorySink);
        services.AddSingleton<ISecretScanner>(secretScanner);
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(options.MinimumLogLevel);
            builder.AddProvider(memorySink);
            if (options.EnableFileLogging)
            {
                builder.AddProvider(new FileLoggerProvider(paths.LogsDirectory, secretScanner.RedactLine, options.MinimumLogLevel));
            }
        });

        services.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(paths.SettingsPath, sp.GetRequiredService<ILogger<JsonSettingsStore>>()));
        services.AddSingleton(new SqliteConnectionFactory(paths.IndexDatabasePath));
        services.AddSingleton<IIndexStore, SqliteIndexStore>();
        services.AddSingleton<ISearchService, SqliteSearchService>();
        services.AddSingleton<ITextFileReader, TextFileReader>();
        services.AddSingleton<IRepositoryScanner, FileSystemRepositoryScanner>();
        services.AddSingleton<IWorkspaceDiscovery, WorkspaceDiscovery>();
        services.AddSingleton<ISourceAnalyzer, CSharpSyntaxAnalyzer>();
        services.AddSingleton<IGitMetadataReader, GitHeadReader>();
        services.AddSingleton<IndexingService>();
        services.AddSingleton<IExportService, ExportService>();
        return services;
    }
}
