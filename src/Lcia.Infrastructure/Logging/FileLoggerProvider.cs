using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Lcia.Infrastructure;

/// <summary>
/// Logger su file giornaliero (lcia-AAAAMMGG.log) nella cartella dati dell'applicazione.
/// Ogni riga passa dalla funzione di oscuramento: i log non contengono credenziali.
/// I contenuti dei file analizzati non vengono mai registrati.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly Func<string, string> _sanitize;
    private readonly LogLevel _minimumLevel;
    private readonly Lock _lock = new();

    public FileLoggerProvider(string directory, Func<string, string> sanitize, LogLevel minimumLevel = LogLevel.Information, int retentionDays = 14)
    {
        _directory = directory;
        _sanitize = sanitize;
        _minimumLevel = minimumLevel;
        try
        {
            Directory.CreateDirectory(directory);
            DeleteOldLogs(retentionDays);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Il logging non deve mai impedire l'avvio dell'applicazione.
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var builder = new StringBuilder();
        builder.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
            .Append(" [").Append(LevelCode(level)).Append("] ")
            .Append(ShortCategory(category)).Append(": ")
            .Append(_sanitize(message));
        if (exception is not null)
        {
            builder.AppendLine().Append("    ").Append(_sanitize(exception.ToString()).Replace("\n", "\n    ", StringComparison.Ordinal));
        }

        builder.AppendLine();
        var path = Path.Combine(_directory, $"lcia-{DateTime.Now:yyyyMMdd}.log");
        lock (_lock)
        {
            try
            {
                File.AppendAllText(path, builder.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignorato: il log è diagnostico.
            }
        }
    }

    private void DeleteOldLogs(int retentionDays)
    {
        var limit = DateTime.Now.AddDays(-Math.Max(1, retentionDays));
        foreach (var file in Directory.EnumerateFiles(_directory, "lcia-*.log"))
        {
            if (File.GetLastWriteTime(file) < limit)
            {
                File.Delete(file);
            }
        }
    }

    internal static string LevelCode(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "---",
    };

    internal static string ShortCategory(string category)
    {
        var index = category.LastIndexOf('.');
        return index < 0 ? category : category[(index + 1)..];
    }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= owner._minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            owner.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}
