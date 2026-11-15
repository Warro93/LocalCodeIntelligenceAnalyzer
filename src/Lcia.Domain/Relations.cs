namespace Lcia.Domain;

public enum RelationKind
{
    /// <summary>Tipo base dichiarato (classe base o interfaccia: senza semantica non si distingue).</summary>
    BaseType,

    /// <summary>Tipo di un parametro del costruttore (anche costruttore primario).</summary>
    ConstructorInjection,

    /// <summary>Tipo di un campo o di una proprietà.</summary>
    MemberType,

    /// <summary>Invocazione di un metodo identificata per nome.</summary>
    Invocation,

    /// <summary>Creazione di un'istanza con <c>new</c>.</summary>
    ObjectCreation,

    /// <summary>Registrazione in un contenitore di dependency injection.</summary>
    DiRegistration,

    /// <summary>Direttiva <c>using</c> di namespace.</summary>
    UsingNamespace,
}

/// <summary>
/// Livello di evidenza di una relazione. Nessuna relazione viene dichiarata
/// "verificata" se non è stata risolta semanticamente.
/// </summary>
public enum EvidenceLevel
{
    /// <summary>Diretta e verificata (risolta dal modello semantico).</summary>
    DirectVerified,

    /// <summary>Indiretta ma verificata (catena di relazioni verificate).</summary>
    IndirectVerified,

    /// <summary>Riferimento testuale (corrispondenza di nome senza risoluzione).</summary>
    TextualReference,

    /// <summary>Inferita con evidenza sintattica (dichiarazione presente, nome non risolto).</summary>
    InferredWithEvidence,

    /// <summary>Dinamica o non determinabile staticamente (reflection, SQL dinamico, ...).</summary>
    Dynamic,
}

/// <summary>Relazione tra un simbolo sorgente e un tipo o membro di destinazione.</summary>
public sealed record SymbolRelation
{
    public long Id { get; init; }
    public long FileId { get; init; }

    /// <summary>Solo durante l'analisi: indice del simbolo sorgente nella lista dell'analizzatore.</summary>
    public int? SourceSymbolIndex { get; init; }

    public long? SourceSymbolId { get; init; }
    public required string SourceName { get; init; }

    /// <summary>Testo della destinazione così come compare nel codice.</summary>
    public required string TargetName { get; init; }

    /// <summary>Nome semplice della destinazione (senza namespace e argomenti generici).</summary>
    public required string TargetSimpleName { get; init; }

    public long? TargetSymbolId { get; init; }
    public RelationKind Kind { get; init; }
    public EvidenceLevel Evidence { get; init; }

    /// <summary>Riga (1-based) in cui si trova l'evidenza.</summary>
    public int Line { get; init; }

    /// <summary>Descrizione breve dell'evidenza (mai segreti né contenuti estesi).</summary>
    public string? EvidenceText { get; init; }
}

public static class RelationExtensions
{
    public static string ToItalian(this EvidenceLevel level) => level switch
    {
        EvidenceLevel.DirectVerified => "Diretta e verificata",
        EvidenceLevel.IndirectVerified => "Indiretta ma verificata",
        EvidenceLevel.TextualReference => "Riferimento testuale",
        EvidenceLevel.InferredWithEvidence => "Inferita con evidenza",
        EvidenceLevel.Dynamic => "Dinamica / non determinabile",
        _ => level.ToString(),
    };

    public static string ToItalian(this RelationKind kind) => kind switch
    {
        RelationKind.BaseType => "Tipo base",
        RelationKind.ConstructorInjection => "Dipendenza (costruttore)",
        RelationKind.MemberType => "Dipendenza (campo/proprietà)",
        RelationKind.Invocation => "Invocazione",
        RelationKind.ObjectCreation => "Creazione istanza",
        RelationKind.DiRegistration => "Registrazione DI",
        RelationKind.UsingNamespace => "Using namespace",
        _ => kind.ToString(),
    };

    /// <summary>
    /// Ricava il nome semplice da un nome di tipo o espressione:
    /// <c>System.Collections.Generic.List&lt;int&gt;</c> diventa <c>List</c>, <c>IFoo?</c> diventa <c>IFoo</c>.
    /// </summary>
    public static string ToSimpleName(string typeOrExpression)
    {
        var text = typeOrExpression.Trim();
        var generic = text.IndexOf('<');
        if (generic >= 0)
        {
            text = text[..generic];
        }

        var paren = text.IndexOf('(');
        if (paren >= 0)
        {
            text = text[..paren];
        }

        text = text.TrimEnd('?', ']', '[', '*', ' ');
        var alias = text.LastIndexOf("::", StringComparison.Ordinal);
        if (alias >= 0)
        {
            text = text[(alias + 2)..];
        }

        var dot = text.LastIndexOf('.');
        return dot >= 0 ? text[(dot + 1)..] : text;
    }
}
