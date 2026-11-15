using Lcia.Domain;

namespace Lcia.Application;

/// <summary>Risultato dell'analisi di un singolo file sorgente.</summary>
public sealed record SourceAnalysisResult(
    IReadOnlyList<CodeSymbol> Symbols,
    IReadOnlyList<SymbolRelation> Relations,
    SupportLevel Level,
    IReadOnlyList<string> Diagnostics)
{
    public static SourceAnalysisResult Empty { get; } = new([], [], SupportLevel.TextOnly, []);
}

/// <summary>Analizzatore di un linguaggio (Code Analysis Engine). Deve essere thread-safe.</summary>
public interface ISourceAnalyzer
{
    string Name { get; }

    bool CanAnalyze(SourceLanguage language);

    SourceAnalysisResult Analyze(string relativePath, string text, CancellationToken cancellationToken);
}

/// <summary>Versione del formato dei dati di analisi: se cambia, i file vengono rianalizzati.</summary>
public static class IndexFormat
{
    public const int Version = 1;

    /// <summary>Righe per blocco dell'indice full-text.</summary>
    public const int ChunkLines = 40;
}
