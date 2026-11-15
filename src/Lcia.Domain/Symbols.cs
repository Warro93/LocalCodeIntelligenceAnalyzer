namespace Lcia.Domain;

public enum SymbolKind
{
    Namespace,
    Class,
    Interface,
    Struct,
    Record,
    RecordStruct,
    Enum,
    Delegate,
    Method,
    Constructor,
    Destructor,
    Property,
    Indexer,
    Field,
    Constant,
    Event,
    Operator,
    EnumMember,
}

/// <summary>Simbolo dichiarato in un file sorgente (classe, metodo, proprietà, ...).</summary>
public sealed record CodeSymbol
{
    public long Id { get; init; }
    public long FileId { get; init; }

    /// <summary>Id del simbolo contenitore (tipo o namespace), se presente.</summary>
    public long? ParentId { get; init; }

    /// <summary>Solo durante l'analisi: indice del contenitore nella lista prodotta dall'analizzatore.</summary>
    public int? ParentIndex { get; init; }

    public SymbolKind Kind { get; init; }
    public required string Name { get; init; }
    public required string FullName { get; init; }
    public string? ContainerName { get; init; }
    public string? Namespace { get; init; }
    public string? Signature { get; init; }
    public string? Accessibility { get; init; }
    public string? Modifiers { get; init; }

    /// <summary>Prima riga (1-based) della dichiarazione, attributi inclusi.</summary>
    public int StartLine { get; init; }

    /// <summary>Ultima riga (1-based) della dichiarazione.</summary>
    public int EndLine { get; init; }

    public string? Documentation { get; init; }
    public string? Attributes { get; init; }

    public bool IsType => Kind is SymbolKind.Class or SymbolKind.Interface or SymbolKind.Struct
        or SymbolKind.Record or SymbolKind.RecordStruct or SymbolKind.Enum or SymbolKind.Delegate;
}

public static class SymbolKindExtensions
{
    public static string ToItalian(this SymbolKind kind) => kind switch
    {
        SymbolKind.Namespace => "Namespace",
        SymbolKind.Class => "Classe",
        SymbolKind.Interface => "Interfaccia",
        SymbolKind.Struct => "Struct",
        SymbolKind.Record => "Record",
        SymbolKind.RecordStruct => "Record struct",
        SymbolKind.Enum => "Enum",
        SymbolKind.Delegate => "Delegate",
        SymbolKind.Method => "Metodo",
        SymbolKind.Constructor => "Costruttore",
        SymbolKind.Destructor => "Distruttore",
        SymbolKind.Property => "Proprietà",
        SymbolKind.Indexer => "Indicizzatore",
        SymbolKind.Field => "Campo",
        SymbolKind.Constant => "Costante",
        SymbolKind.Event => "Evento",
        SymbolKind.Operator => "Operatore",
        SymbolKind.EnumMember => "Membro enum",
        _ => kind.ToString(),
    };

    /// <summary>Etichetta inglese stabile usata nei dossier esportati (es. CLASS, INTERFACE).</summary>
    public static string ToExportLabel(this SymbolKind kind) => kind switch
    {
        SymbolKind.RecordStruct => "RECORD STRUCT",
        SymbolKind.EnumMember => "ENUM MEMBER",
        _ => kind.ToString().ToUpperInvariant(),
    };
}
