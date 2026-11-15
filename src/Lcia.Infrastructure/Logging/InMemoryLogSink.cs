using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Lcia.Infrastructure;

public sealed record LogEntry(long Sequence, DateTimeOffset Timestamp, LogLevel Level, string Category, string Message);

/// <summary>
/// Ultime voci di log in memoria, consultabili dall'interfaccia (schermata Log).
/// Thread-safe; i messaggi vengono oscurati come nel log su file.
/// </summary>
public sealed class InMemoryLogSink : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private readonly int _capacity;
    private readonly Func<string, string> _sanitize;
    private long _sequence;

    public InMemoryLogSink(int capacity, Func<string, string> sanitize)
    {
        _capacity = Math.Max(100, capacity);
        _sanitize = sanitize;
    }

    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    public long LastSequence => Interlocked.Read(ref _sequence);

    /// <summary>Voci con numero di sequenza maggiore di <paramref name="afterSequence"/>.</summary>
    public IReadOnlyList<LogEntry> GetEntriesAfter(long afterSequence) =>
        _entries.Where(e => e.Sequence > afterSequence).OrderBy(e => e.Sequence).ToArray();

    public ILogger CreateLogger(string categoryName) => new MemoryLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Add(LogLevel level, string category, string message, Exception? exception)
    {
        var text = exception is null ? message : $"{message} — {exception.GetType().Name}: {exception.Message}";
        var entry = new LogEntry(Interlocked.Increment(ref _sequence), DateTimeOffset.Now, level, FileLoggerProvider.ShortCategory(category), _sanitize(text));
        _entries.Enqueue(entry);
        while (_entries.Count > _capacity && _entries.TryDequeue(out _))
        {
        }
    }

    private sealed class MemoryLogger(InMemoryLogSink owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= owner.MinimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                owner.Add(logLevel, category, formatter(state, exception), exception);
            }
        }
    }
}
