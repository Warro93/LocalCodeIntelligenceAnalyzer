using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lcia.Analysis.CSharp;

/// <summary>
/// Decide se un tipo è una dipendenza "interessante" (servizi, repository, modelli del dominio)
/// escludendo tipi primitivi e tipi comuni della libreria standard.
/// </summary>
internal static class TypeNameFilter
{
    private static readonly HashSet<string> CommonSimpleTypes = new(StringComparer.Ordinal)
    {
        "String", "Object", "Boolean", "Byte", "SByte", "Char", "Decimal", "Double", "Single", "Int16", "Int32", "Int64",
        "UInt16", "UInt32", "UInt64", "IntPtr", "UIntPtr", "DateTime", "DateTimeOffset", "DateOnly", "TimeOnly", "TimeSpan",
        "Guid", "Uri", "Version", "Type", "Exception", "CancellationToken", "Task", "ValueTask", "Stream", "StringBuilder",
        "Encoding", "Regex", "Random", "IDisposable", "IAsyncDisposable", "EventHandler", "EventArgs", "Action", "Func",
        "CultureInfo", "Half", "BigInteger", "Index", "Range", "dynamic", "var",
    };

    private static readonly HashSet<string> WrapperGenericTypes = new(StringComparer.Ordinal)
    {
        "List", "IList", "IEnumerable", "IReadOnlyList", "IReadOnlyCollection", "ICollection", "Collection", "ReadOnlyCollection",
        "Dictionary", "IDictionary", "IReadOnlyDictionary", "ConcurrentDictionary", "HashSet", "ISet", "IReadOnlySet", "SortedSet",
        "SortedDictionary", "Queue", "Stack", "ConcurrentQueue", "ConcurrentBag", "LinkedList", "ObservableCollection",
        "Task", "ValueTask", "Func", "Action", "Nullable", "Lazy", "IAsyncEnumerable", "IEnumerator", "ImmutableArray",
        "ImmutableList", "ImmutableDictionary", "ImmutableHashSet", "Span", "ReadOnlySpan", "Memory", "ReadOnlyMemory",
        "KeyValuePair", "Tuple", "ValueTuple", "EventHandler", "Predicate", "Expression", "IQueryable", "Channel",
    };

    public static bool IsInteresting(TypeSyntax? type) => type switch
    {
        null => false,
        PredefinedTypeSyntax => false,
        NullableTypeSyntax nullable => IsInteresting(nullable.ElementType),
        ArrayTypeSyntax array => IsInteresting(array.ElementType),
        RefTypeSyntax reference => IsInteresting(reference.Type),
        PointerTypeSyntax => false,
        FunctionPointerTypeSyntax => false,
        TupleTypeSyntax tuple => tuple.Elements.Any(e => IsInteresting(e.Type)),
        QualifiedNameSyntax qualified => IsInteresting(qualified.Right),
        AliasQualifiedNameSyntax alias => IsInteresting(alias.Name),
        GenericNameSyntax generic => WrapperGenericTypes.Contains(generic.Identifier.ValueText)
            ? generic.TypeArgumentList.Arguments.Any(IsInteresting)
            : true,
        IdentifierNameSyntax identifier => !CommonSimpleTypes.Contains(identifier.Identifier.ValueText),
        _ => true,
    };
}
