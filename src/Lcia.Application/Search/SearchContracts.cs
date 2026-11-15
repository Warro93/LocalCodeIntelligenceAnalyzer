using Lcia.Domain;

namespace Lcia.Application;

public enum SearchMode
{
    /// <summary>Parole chiave sull'indice full-text (FTS5), con frasi tra virgolette, prefissi (parola*) ed esclusioni (-parola).</summary>
    Words,

    /// <summary>Testo esatto (sottostringa), opzionalmente sensibile alle maiuscole.</summary>
    ExactText,

    /// <summary>Espressione regolare .NET applicata riga per riga.</summary>
    Regex,

    /// <summary>Nomi di simboli (classi, metodi, proprietà, ...). Accetta * come carattere jolly.</summary>
    Symbol,

    /// <summary>Riferimenti sintattici a un tipo o membro (dipendenze, tipi base, registrazioni DI, invocazioni).</summary>
    References,
}

public enum SearchHitKind
{
    Content,
    Symbol,
    Reference,
}

/// <summary>Query di ricerca: tutti i filtri sono combinabili.</summary>
public sealed record SearchQuery
{
    public required long RepositoryId { get; init; }
    public required string Text { get; init; }
    public SearchMode Mode { get; init; } = SearchMode.Words;
    public bool CaseSensitive { get; init; }

    /// <summary>Estensioni ammesse (es. ".cs", ".sql"); vuoto = tutte.</summary>
    public IReadOnlyCollection<string> Extensions { get; init; } = [];

    /// <summary>Testo che deve comparire nel percorso relativo.</summary>
    public string? PathContains { get; init; }

    public IReadOnlyCollection<SourceLanguage> Languages { get; init; } = [];

    /// <summary>Nome del progetto (.csproj, ...) a cui il file appartiene.</summary>
    public string? ProjectName { get; init; }

    /// <summary>Tipi di simbolo ammessi (solo modalità Symbol).</summary>
    public IReadOnlyCollection<SymbolKind> SymbolKinds { get; init; } = [];

    /// <summary>Tipi di relazione ammessi (solo modalità References).</summary>
    public IReadOnlyCollection<RelationKind> RelationKinds { get; init; } = [];

    public int MaxResults { get; init; } = 500;
}

public sealed record SearchHit(
    string RelativePath,
    int Line,
    string Preview,
    SearchHitKind Kind,
    string? Detail,
    string? ProjectName,
    double Score);

public sealed record SearchResult(IReadOnlyList<SearchHit> Hits, bool Truncated, TimeSpan Elapsed, string? Warning)
{
    public static SearchResult Empty(string? warning = null) => new([], false, TimeSpan.Zero, warning);
}

/// <summary>Search Engine sull'indice locale.</summary>
public interface ISearchService
{
    Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default);
}
