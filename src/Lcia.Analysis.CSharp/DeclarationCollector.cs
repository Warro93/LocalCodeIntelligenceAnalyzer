using System.Text;
using System.Text.RegularExpressions;
using Lcia.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using DomainSymbolKind = Lcia.Domain.SymbolKind;

namespace Lcia.Analysis.CSharp;

/// <summary>
/// Visita l'albero sintattico raccogliendo dichiarazioni (con righe, firme, documentazione)
/// e relazioni sintattiche con la relativa evidenza.
/// </summary>
internal sealed partial class DeclarationCollector : CSharpSyntaxWalker
{
    private const int MaxSignatureLength = 1000;
    private const int MaxEvidenceLength = 300;

    private static readonly HashSet<string> DiRegistrationMethods = new(StringComparer.Ordinal)
    {
        "AddScoped", "AddTransient", "AddSingleton",
        "TryAddScoped", "TryAddTransient", "TryAddSingleton",
        "AddKeyedScoped", "AddKeyedTransient", "AddKeyedSingleton",
        "TryAddKeyedScoped", "TryAddKeyedTransient", "TryAddKeyedSingleton",
        "AddHostedService", "AddHttpClient", "AddDbContext", "AddDbContextFactory",
    };

    private readonly string _relativePath;
    private readonly CancellationToken _cancellationToken;
    private readonly List<CodeSymbol> _symbols = [];
    private readonly List<SymbolRelation> _relations = [];
    private readonly Stack<string> _namespaces = new();
    private readonly Stack<TypeFrame> _types = new();
    private readonly Stack<int> _containers = new();
    private readonly Stack<int> _members = new();
    private int _visitedNodes;

    public DeclarationCollector(string relativePath, CancellationToken cancellationToken)
        : base(SyntaxWalkerDepth.Node)
    {
        _relativePath = relativePath;
        _cancellationToken = cancellationToken;
    }

    public IReadOnlyList<CodeSymbol> Symbols => _symbols;

    public IReadOnlyList<SymbolRelation> Relations => _relations;

    private string CurrentNamespace => _namespaces.Count == 0 ? string.Empty : _namespaces.Peek();

    private string CurrentContainer => _types.Count > 0 ? _types.Peek().FullName : CurrentNamespace;

    private sealed record TypeFrame(int SymbolIndex, string FullName, bool IsInterface);

    public override void Visit(SyntaxNode? node)
    {
        if ((++_visitedNodes & 0x3FF) == 0)
        {
            _cancellationToken.ThrowIfCancellationRequested();
        }

        base.Visit(node);
    }

    // ------------------------------------------------------------------ namespace

    public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node) =>
        VisitNamespace(node, node.Name.ToString(), () => base.VisitNamespaceDeclaration(node));

    public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node) =>
        VisitNamespace(node, node.Name.ToString(), () => base.VisitFileScopedNamespaceDeclaration(node));

    private void VisitNamespace(BaseNamespaceDeclarationSyntax node, string name, Action visitChildren)
    {
        var fullName = Combine(CurrentNamespace, Normalize(name));
        var index = AddSymbol(DomainSymbolKind.Namespace, fullName, fullName, null, node, default, default, "namespace " + fullName, "public", isNamespace: true);
        _namespaces.Push(fullName);
        _containers.Push(index);
        try
        {
            visitChildren();
        }
        finally
        {
            _containers.Pop();
            _namespaces.Pop();
        }
    }

    public override void VisitUsingDirective(UsingDirectiveSyntax node)
    {
        var name = node.Name?.ToString();
        if (!string.IsNullOrEmpty(name) && node.Alias is null)
        {
            AddRelation(null, _relativePath, Normalize(name), RelationKind.UsingNamespace, EvidenceLevel.InferredWithEvidence, LineOf(node),
                node.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) ? "using static" : "using");
        }

        base.VisitUsingDirective(node);
    }

    // ------------------------------------------------------------------ tipi

    public override void VisitClassDeclaration(ClassDeclarationSyntax node) =>
        VisitType(node, DomainSymbolKind.Class, "class", () => base.VisitClassDeclaration(node));

    public override void VisitStructDeclaration(StructDeclarationSyntax node) =>
        VisitType(node, DomainSymbolKind.Struct, "struct", () => base.VisitStructDeclaration(node));

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) =>
        VisitType(node, DomainSymbolKind.Interface, "interface", () => base.VisitInterfaceDeclaration(node));

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        var isStruct = node.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword);
        VisitType(node, isStruct ? DomainSymbolKind.RecordStruct : DomainSymbolKind.Record, isStruct ? "record struct" : "record",
            () => base.VisitRecordDeclaration(node));
    }

    public override void VisitEnumDeclaration(EnumDeclarationSyntax node) =>
        VisitType(node, DomainSymbolKind.Enum, "enum", () => base.VisitEnumDeclaration(node));

    private void VisitType(BaseTypeDeclarationSyntax node, DomainSymbolKind kind, string keyword, Action visitChildren)
    {
        var name = node.Identifier.ValueText;
        var typeParameters = (node as TypeDeclarationSyntax)?.TypeParameterList?.ToString() ?? string.Empty;
        var primaryParameters = node.ChildNodes().OfType<ParameterListSyntax>().FirstOrDefault();
        var baseList = node.BaseList is null ? string.Empty : " : " + string.Join(", ", node.BaseList.Types.Select(t => t.Type.ToString()));
        var signature = $"{Modifiers(node.Modifiers)} {keyword} {name}{typeParameters}{primaryParameters}{baseList}";

        var container = CurrentContainer;
        var fullName = Combine(container, name);
        var defaultAccessibility = _types.Count > 0 ? "private" : "internal";
        var index = AddSymbol(kind, name, fullName, container, node, node.Modifiers, node.AttributeLists, signature, defaultAccessibility);

        if (node.BaseList is not null)
        {
            foreach (var baseType in node.BaseList.Types)
            {
                AddTypeRelation(index, fullName, baseType.Type, RelationKind.BaseType, EvidenceLevel.InferredWithEvidence,
                    "Tipo base dichiarato (classe base o interfaccia; nome non risolto semanticamente)", requireInteresting: false);
            }
        }

        if (primaryParameters is not null)
        {
            foreach (var parameter in primaryParameters.Parameters)
            {
                if (parameter.Type is not null)
                {
                    AddTypeRelation(index, fullName, parameter.Type, RelationKind.ConstructorInjection, EvidenceLevel.InferredWithEvidence,
                        $"Parametro '{parameter.Identifier.ValueText}' del costruttore primario");
                }
            }
        }

        _types.Push(new TypeFrame(index, fullName, kind == DomainSymbolKind.Interface));
        _containers.Push(index);
        try
        {
            visitChildren();
        }
        finally
        {
            _containers.Pop();
            _types.Pop();
        }
    }

    public override void VisitDelegateDeclaration(DelegateDeclarationSyntax node)
    {
        var name = node.Identifier.ValueText;
        var signature = $"{Modifiers(node.Modifiers)} delegate {node.ReturnType} {name}{node.TypeParameterList}{node.ParameterList}";
        var container = CurrentContainer;
        AddSymbol(DomainSymbolKind.Delegate, name, Combine(container, name), container, node, node.Modifiers, node.AttributeLists, signature,
            _types.Count > 0 ? "private" : "internal");
        base.VisitDelegateDeclaration(node);
    }

    // ------------------------------------------------------------------ membri

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        var name = node.Identifier.ValueText;
        if (node.ExplicitInterfaceSpecifier is not null)
        {
            name = node.ExplicitInterfaceSpecifier.Name + "." + name;
        }

        var signature = $"{Modifiers(node.Modifiers)} {node.ReturnType} {name}{node.TypeParameterList}{node.ParameterList}";
        VisitMember(node, DomainSymbolKind.Method, name, node.Modifiers, node.AttributeLists, signature, () => base.VisitMethodDeclaration(node));
    }

    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        var name = node.Identifier.ValueText;
        var signature = $"{Modifiers(node.Modifiers)} {name}{node.ParameterList}";
        var isStatic = node.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
        if (!isStatic && _types.Count > 0)
        {
            var type = _types.Peek();
            foreach (var parameter in node.ParameterList.Parameters)
            {
                if (parameter.Type is not null)
                {
                    AddTypeRelation(type.SymbolIndex, type.FullName, parameter.Type, RelationKind.ConstructorInjection, EvidenceLevel.InferredWithEvidence,
                        $"Parametro '{parameter.Identifier.ValueText}' del costruttore");
                }
            }
        }

        VisitMember(node, DomainSymbolKind.Constructor, name, node.Modifiers, node.AttributeLists, signature, () => base.VisitConstructorDeclaration(node));
    }

    public override void VisitDestructorDeclaration(DestructorDeclarationSyntax node)
    {
        var name = "~" + node.Identifier.ValueText;
        VisitMember(node, DomainSymbolKind.Destructor, name, node.Modifiers, node.AttributeLists, name + "()", () => base.VisitDestructorDeclaration(node));
    }

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        var name = node.Identifier.ValueText;
        if (node.ExplicitInterfaceSpecifier is not null)
        {
            name = node.ExplicitInterfaceSpecifier.Name + "." + name;
        }

        var signature = $"{Modifiers(node.Modifiers)} {node.Type} {name} {Accessors(node.AccessorList, node.ExpressionBody is not null)}";
        AddMemberTypeRelation(node.Type, $"Tipo della proprietà '{name}'");
        VisitMember(node, DomainSymbolKind.Property, name, node.Modifiers, node.AttributeLists, signature, () => base.VisitPropertyDeclaration(node));
    }

    public override void VisitIndexerDeclaration(IndexerDeclarationSyntax node)
    {
        var signature = $"{Modifiers(node.Modifiers)} {node.Type} this{node.ParameterList} {Accessors(node.AccessorList, node.ExpressionBody is not null)}";
        VisitMember(node, DomainSymbolKind.Indexer, "this[]", node.Modifiers, node.AttributeLists, signature, () => base.VisitIndexerDeclaration(node));
    }

    public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        var isConstant = node.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword));
        VisitVariables(node, node.Declaration, isConstant ? DomainSymbolKind.Constant : DomainSymbolKind.Field, () => base.VisitFieldDeclaration(node));
    }

    public override void VisitEventFieldDeclaration(EventFieldDeclarationSyntax node) =>
        VisitVariables(node, node.Declaration, DomainSymbolKind.Event, () => base.VisitEventFieldDeclaration(node));

    public override void VisitEventDeclaration(EventDeclarationSyntax node)
    {
        var name = node.Identifier.ValueText;
        var signature = $"{Modifiers(node.Modifiers)} event {node.Type} {name}";
        AddMemberTypeRelation(node.Type, $"Tipo dell'evento '{name}'");
        VisitMember(node, DomainSymbolKind.Event, name, node.Modifiers, node.AttributeLists, signature, () => base.VisitEventDeclaration(node));
    }

    public override void VisitOperatorDeclaration(OperatorDeclarationSyntax node)
    {
        var name = "operator " + node.OperatorToken.ValueText;
        var signature = $"{Modifiers(node.Modifiers)} {node.ReturnType} {name}{node.ParameterList}";
        VisitMember(node, DomainSymbolKind.Operator, name, node.Modifiers, node.AttributeLists, signature, () => base.VisitOperatorDeclaration(node));
    }

    public override void VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
    {
        var name = $"{node.ImplicitOrExplicitKeyword.ValueText} operator {node.Type}";
        var signature = $"{Modifiers(node.Modifiers)} {name}{node.ParameterList}";
        VisitMember(node, DomainSymbolKind.Operator, name, node.Modifiers, node.AttributeLists, signature, () => base.VisitConversionOperatorDeclaration(node));
    }

    public override void VisitEnumMemberDeclaration(EnumMemberDeclarationSyntax node)
    {
        var name = node.Identifier.ValueText;
        var signature = node.EqualsValue is null ? name : $"{name} = {node.EqualsValue.Value}";
        var container = CurrentContainer;
        AddSymbol(DomainSymbolKind.EnumMember, name, Combine(container, name), container, node, node.Modifiers, node.AttributeLists, signature, "public");
        base.VisitEnumMemberDeclaration(node);
    }

    private void VisitVariables(MemberDeclarationSyntax node, VariableDeclarationSyntax declaration, DomainSymbolKind kind, Action visitChildren)
    {
        var container = CurrentContainer;
        var keyword = kind == DomainSymbolKind.Event ? "event " : string.Empty;
        var firstIndex = -1;
        foreach (var variable in declaration.Variables)
        {
            var name = variable.Identifier.ValueText;
            var signature = $"{Modifiers(node.Modifiers)} {keyword}{declaration.Type} {name}";
            var index = AddSymbol(kind, name, Combine(container, name), container, node, node.Modifiers, node.AttributeLists, signature, DefaultMemberAccessibility());
            if (firstIndex < 0)
            {
                firstIndex = index;
            }
        }

        AddMemberTypeRelation(declaration.Type, $"Tipo di '{string.Join(", ", declaration.Variables.Select(v => v.Identifier.ValueText))}'");

        if (firstIndex >= 0)
        {
            _members.Push(firstIndex);
        }

        try
        {
            visitChildren();
        }
        finally
        {
            if (firstIndex >= 0)
            {
                _members.Pop();
            }
        }
    }

    private void VisitMember(MemberDeclarationSyntax node, DomainSymbolKind kind, string name, SyntaxTokenList modifiers,
        SyntaxList<AttributeListSyntax> attributes, string signature, Action visitChildren)
    {
        var container = CurrentContainer;
        var index = AddSymbol(kind, name, Combine(container, name), container, node, modifiers, attributes, signature, DefaultMemberAccessibility());
        _members.Push(index);
        try
        {
            visitChildren();
        }
        finally
        {
            _members.Pop();
        }
    }

    // ------------------------------------------------------------------ espressioni

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var nameSyntax = InvokedName(node.Expression);
        var invokedName = nameSyntax?.Identifier.ValueText;
        if (!string.IsNullOrEmpty(invokedName) && invokedName != "nameof")
        {
            if (DiRegistrationMethods.Contains(invokedName))
            {
                AddDiRegistration(node, invokedName, nameSyntax);
            }

            if (CurrentSourceIndex() is int source)
            {
                var target = Truncate(Normalize(node.Expression.ToString()), 200);
                AddRelation(source, _symbols[source].FullName, target, RelationKind.Invocation, EvidenceLevel.TextualReference, LineOf(node),
                    "Invocazione per nome (destinazione non risolta)", invokedName);
            }
        }

        base.VisitInvocationExpression(node);
    }

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        if (CurrentSourceIndex() is int source && TypeNameFilter.IsInteresting(node.Type))
        {
            AddTypeRelation(source, _symbols[source].FullName, node.Type, RelationKind.ObjectCreation, EvidenceLevel.TextualReference,
                "Creazione di istanza con 'new' (tipo non risolto)");
        }

        base.VisitObjectCreationExpression(node);
    }

    private void AddDiRegistration(InvocationExpressionSyntax node, string method, SimpleNameSyntax? nameSyntax)
    {
        TypeSyntax? service = null;
        TypeSyntax? implementation = null;
        if (nameSyntax is GenericNameSyntax generic)
        {
            var arguments = generic.TypeArgumentList.Arguments;
            if (arguments.Count >= 2)
            {
                service = arguments[0];
                implementation = arguments[1];
            }
            else if (arguments.Count == 1)
            {
                service = arguments[0];
                implementation = arguments[0];
            }
        }
        else
        {
            var typeOfs = node.ArgumentList.Arguments.Select(a => a.Expression).OfType<TypeOfExpressionSyntax>().Select(t => t.Type).ToList();
            if (typeOfs.Count >= 2)
            {
                service = typeOfs[0];
                implementation = typeOfs[1];
            }
            else if (typeOfs.Count == 1)
            {
                service = typeOfs[0];
                implementation = typeOfs[0];
            }
        }

        if (service is null || implementation is null)
        {
            return;
        }

        var serviceText = Normalize(service.ToString());
        var implementationText = Normalize(implementation.ToString());
        var hasFactory = node.ArgumentList.Arguments.Any(a => a.Expression is LambdaExpressionSyntax);
        var evidence = $"{method}: {serviceText} -> {implementationText}" + (hasFactory ? " (factory: implementazione determinata a runtime)" : string.Empty);
        AddRelation(CurrentSourceIndex(), serviceText, implementationText, RelationKind.DiRegistration,
            hasFactory ? EvidenceLevel.Dynamic : EvidenceLevel.InferredWithEvidence, LineOf(node), evidence);
    }

    private static SimpleNameSyntax? InvokedName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
        MemberBindingExpressionSyntax memberBinding => memberBinding.Name,
        SimpleNameSyntax simpleName => simpleName,
        _ => null,
    };

    // ------------------------------------------------------------------ registrazione simboli e relazioni

    private int AddSymbol(DomainSymbolKind kind, string name, string fullName, string? container, SyntaxNode node,
        SyntaxTokenList modifiers, SyntaxList<AttributeListSyntax> attributes, string signature, string defaultAccessibility, bool isNamespace = false)
    {
        var span = node.GetLocation().GetLineSpan();
        _symbols.Add(new CodeSymbol
        {
            Kind = kind,
            Name = name,
            FullName = fullName,
            ContainerName = string.IsNullOrEmpty(container) ? null : container,
            Namespace = isNamespace ? fullName : (string.IsNullOrEmpty(CurrentNamespace) ? null : CurrentNamespace),
            Signature = Truncate(Normalize(signature), MaxSignatureLength),
            Accessibility = AccessibilityOf(modifiers, defaultAccessibility),
            Modifiers = ModifierList(modifiers),
            StartLine = span.StartLinePosition.Line + 1,
            EndLine = span.EndLinePosition.Line + 1,
            Documentation = isNamespace ? null : DocumentationExtractor.Extract(node),
            Attributes = AttributesText(attributes),
            ParentIndex = _containers.Count > 0 ? _containers.Peek() : null,
        });
        return _symbols.Count - 1;
    }

    private void AddTypeRelation(int? sourceIndex, string sourceName, TypeSyntax type, RelationKind kind, EvidenceLevel evidence, string evidenceText,
        bool requireInteresting = true)
    {
        if (requireInteresting && !TypeNameFilter.IsInteresting(type))
        {
            return;
        }

        AddRelation(sourceIndex, sourceName, Normalize(type.ToString()), kind, evidence, LineOf(type), evidenceText);
    }

    private void AddMemberTypeRelation(TypeSyntax type, string evidenceText)
    {
        if (_types.Count == 0)
        {
            return;
        }

        var owner = _types.Peek();
        AddTypeRelation(owner.SymbolIndex, owner.FullName, type, RelationKind.MemberType, EvidenceLevel.InferredWithEvidence, evidenceText);
    }

    private void AddRelation(int? sourceIndex, string sourceName, string target, RelationKind kind, EvidenceLevel evidence, int line, string evidenceText,
        string? simpleName = null)
    {
        _relations.Add(new SymbolRelation
        {
            SourceSymbolIndex = sourceIndex,
            SourceName = sourceName,
            TargetName = target,
            TargetSimpleName = simpleName ?? RelationExtensions.ToSimpleName(target),
            Kind = kind,
            Evidence = evidence,
            Line = line,
            EvidenceText = Truncate(evidenceText, MaxEvidenceLength),
        });
    }

    private int? CurrentSourceIndex()
    {
        if (_members.Count > 0)
        {
            return _members.Peek();
        }

        return _types.Count > 0 ? _types.Peek().SymbolIndex : null;
    }

    private string DefaultMemberAccessibility() => _types.Count > 0 && _types.Peek().IsInterface ? "public" : "private";

    // ------------------------------------------------------------------ formattazione

    private static int LineOf(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static string Combine(string container, string name) =>
        string.IsNullOrEmpty(container) ? name : container + "." + name;

    private static string Modifiers(SyntaxTokenList modifiers) => string.Join(" ", modifiers.Select(m => m.ValueText));

    private static string? ModifierList(SyntaxTokenList modifiers)
    {
        var list = modifiers
            .Select(m => m.ValueText)
            .Where(m => m is not ("public" or "private" or "protected" or "internal" or "file"))
            .ToArray();
        return list.Length == 0 ? null : string.Join(" ", list);
    }

    private static string AccessibilityOf(SyntaxTokenList modifiers, string defaultAccessibility)
    {
        var parts = modifiers
            .Select(m => m.ValueText)
            .Where(m => m is "public" or "private" or "protected" or "internal" or "file")
            .ToArray();
        return parts.Length == 0 ? defaultAccessibility : string.Join(" ", parts);
    }

    private static string? AttributesText(SyntaxList<AttributeListSyntax> attributeLists)
    {
        var attributes = attributeLists.SelectMany(l => l.Attributes).Select(a => Normalize(a.ToString())).ToArray();
        return attributes.Length == 0 ? null : Truncate(string.Join(", ", attributes), 500);
    }

    private static string Accessors(AccessorListSyntax? accessors, bool expressionBodied)
    {
        if (accessors is null)
        {
            return expressionBodied ? "{ get; }" : string.Empty;
        }

        var builder = new StringBuilder("{ ");
        foreach (var accessor in accessors.Accessors)
        {
            if (accessor.Modifiers.Count > 0)
            {
                builder.Append(accessor.Modifiers.ToString()).Append(' ');
            }

            builder.Append(accessor.Keyword.ValueText).Append("; ");
        }

        return builder.Append('}').ToString();
    }

    internal static string Normalize(string text) => Whitespace().Replace(text, " ").Trim();

    internal static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
