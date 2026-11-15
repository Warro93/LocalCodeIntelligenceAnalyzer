using System.Globalization;
using System.Text;
using Lcia.Application;
using Lcia.Domain;

namespace Lcia.Export;

/// <summary>
/// Composizione del dossier: le sezioni sono identiche in TXT e Markdown,
/// cambiano solo le primitive di rendering implementate dalle sottoclassi.
/// Le etichette sono in inglese (stabili e leggibili da qualsiasi AI).
/// </summary>
internal abstract class ExportFormatter
{
    protected static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public abstract string Extension { get; }

    public static ExportFormatter Create(ExportFormat format) => format switch
    {
        ExportFormat.Markdown => new MarkdownExportFormatter(),
        _ => new TextExportFormatter(),
    };

    // ------------------------------------------------------------------ primitive di rendering

    protected abstract void Title(StringBuilder sb, string text);

    protected abstract void Section(StringBuilder sb, string text);

    protected abstract void SubSection(StringBuilder sb, string text);

    protected abstract void Field(StringBuilder sb, string key, string value, int indent = 0);

    protected abstract void ListHeader(StringBuilder sb, string key, int indent = 0);

    protected abstract void Item(StringBuilder sb, string text, int indent = 0);

    /// <summary>Elemento di elenco con una parte di codice (firma, tipo) e un suffisso testuale.</summary>
    protected abstract void CodeItem(StringBuilder sb, string code, string? suffix, int indent = 0);

    protected abstract void Paragraph(StringBuilder sb, string text);

    protected abstract void Code(StringBuilder sb, IReadOnlyList<string> lines, string language);

    protected abstract void FileBanner(StringBuilder sb, string relativePath);

    protected abstract void FileEnd(StringBuilder sb, string relativePath);

    protected abstract void Table(StringBuilder sb, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows);

    public abstract string PartHeader(int partNumber, string baseName, string indexFileName);

    public abstract string ContinuationNotice(string relativePath, int nextPart);

    public abstract string ContinuedNotice(string relativePath, int previousPart);

    // ------------------------------------------------------------------ sezioni

    public string DocumentHeader(ExportHeaderModel model)
    {
        var sb = new StringBuilder();
        var request = model.Request;
        var repository = model.Repository;
        Title(sb, "LOCAL CODE INTELLIGENCE ANALYZER - FULL PROJECT CONTEXT EXPORT");
        Field(sb, "GENERATED (UTC)", model.GeneratedUtc.ToString("yyyy-MM-dd HH:mm:ss", Invariant));
        Field(sb, "TOOL", $"Local Code Intelligence Analyzer {model.ToolVersion}");
        Field(sb, "EXPORT MODE", request.Scope == ExportScope.Full ? "FULL EXPORT" : "CUSTOM EXPORT (user selection)");
        Field(sb, "EXPORT NAME", model.BaseName);
        Field(sb, "REPOSITORY", repository.Name);
        Field(sb, "ROOT PATH", request.IncludeAbsolutePaths
            ? repository.RootPath
            : "(omitted for privacy - all paths below are relative to the repository root)");
        if (!string.IsNullOrEmpty(repository.EntryPath))
        {
            Field(sb, "SOLUTION", request.IncludeAbsolutePaths ? repository.EntryPath : Path.GetFileName(repository.EntryPath));
        }

        Field(sb, "GIT BRANCH", repository.GitBranch ?? "n/a");
        Field(sb, "GIT COMMIT", repository.GitCommit ?? "n/a");
        Field(sb, "LAST INDEXED (UTC)", repository.LastScanUtc?.ToString("yyyy-MM-dd HH:mm:ss", Invariant) ?? "n/a");
        Field(sb, "FILES INCLUDED", model.FileCount.ToString("N0", Invariant));
        Field(sb, "TOTAL LINES", model.TotalLines.ToString("N0", Invariant));
        Field(sb, "SOURCE SIZE", $"{model.SourceBytes.ToString("N0", Invariant)} bytes");
        Field(sb, "ESTIMATED TOKENS", $"~{model.EstimatedTokens.ToString("N0", Invariant)} (approximation: about 4 characters per token; the exact count depends on the model)");
        Field(sb, "SECRET REDACTION", request.RedactSecrets
            ? "ENABLED - values that look like secrets are replaced with [REDACTED:<category>]"
            : "DISABLED by explicit user choice - this document may contain credentials");
        if (request.RedactSecrets)
        {
            Field(sb, "REDACTION SCOPE",
                "passwords, tokens, API keys, private keys, certificates" +
                (request.Redaction.FullConnectionStrings ? ", whole connection strings (server, database, user)" : ", connection string passwords") +
                (request.Redaction.PersonalData ? ", e-mail and IP addresses" : string.Empty));
        }
        Field(sb, "LINE NUMBERS", request.IncludeLineNumbers
            ? "each source line is prefixed with \"N | \" (added by the exporter, not part of the original source)"
            : "not added - source code is reproduced verbatim (line endings normalized to LF)");
        if (request.IncludeExtensions.Count > 0)
        {
            Field(sb, "FILTER EXTENSIONS", string.Join(", ", request.IncludeExtensions));
        }

        if (request.IncludePathPrefixes.Count > 0)
        {
            Field(sb, "FILTER FOLDERS", string.Join(", ", request.IncludePathPrefixes));
        }

        if (request.ExcludePathPrefixes.Count > 0)
        {
            Field(sb, "EXCLUDED FOLDERS", string.Join(", ", request.ExcludePathPrefixes));
        }

        Section(sb, "HOW TO READ THIS DOCUMENT");
        Item(sb, "This is a self-contained technical dossier generated locally by Local Code Intelligence Analyzer. No code was executed to produce it.");
        Item(sb, "Order: header, directory tree, file list, solutions/projects, symbol index, cross references, then the full source of every included file, and an export summary.");
        Item(sb, "Each file starts with a FILE banner followed by metadata and SOURCE CODE, and ends with an END OF FILE marker.");
        Item(sb, "Symbols, dependencies and DI registrations come from static syntactic analysis (Roslyn syntax trees). Type names are NOT resolved to their definitions unless stated otherwise.");
        Item(sb, "Evidence levels: DirectVerified, IndirectVerified (semantic, not produced by this version), InferredWithEvidence (declared in code, name not resolved), TextualReference (name match only), Dynamic (determined at runtime).");
        Item(sb, "Values replaced with [REDACTED:<category>] were removed on purpose: do not try to reconstruct them.");
        Item(sb, "When citing code, reference the relative file path and the line numbers shown here.");
        return sb.ToString();
    }

    public string DirectoryTree(IReadOnlyList<string> paths)
    {
        var sb = new StringBuilder();
        Section(sb, "DIRECTORY TREE (included files)");
        var root = new TreeNode(string.Empty);
        foreach (var path in paths)
        {
            var node = root;
            foreach (var part in path.Split('/'))
            {
                node = node.GetOrAdd(part);
            }
        }

        var lines = new List<string> { "." };
        RenderTree(root, string.Empty, lines);
        Code(sb, lines, "text");
        return sb.ToString();
    }

    public string FileList(IReadOnlyList<IndexedFile> files, Func<long?, string?> projectName)
    {
        var sb = new StringBuilder();
        Section(sb, "FILE LIST");
        var headers = new[] { "#", "Path", "Language", "Project", "Lines", "Bytes", "SHA-256 (first 12)" };
        var rows = files.Select((f, i) => new[]
        {
            (i + 1).ToString(Invariant),
            f.RelativePath,
            LanguageCatalog.Describe(f.Language).DisplayName,
            projectName(f.ProjectId) ?? "-",
            f.LineCount.ToString(Invariant),
            f.SizeBytes.ToString(Invariant),
            f.Sha256 is { Length: >= 12 } sha ? sha[..12] : "-",
        });
        Table(sb, headers, rows);
        return sb.ToString();
    }

    public string Projects(IReadOnlyList<ProjectInfo> projects)
    {
        var sb = new StringBuilder();
        Section(sb, "SOLUTIONS AND PROJECTS");
        if (projects.Count == 0)
        {
            Paragraph(sb, "No solution or project file was found in the analyzed folder.");
            return sb.ToString();
        }

        foreach (var project in projects)
        {
            SubSection(sb, $"{(project.Kind == ProjectKind.Solution ? "SOLUTION" : "PROJECT")}: {project.RelativePath}");
            if (project.Kind == ProjectKind.Project)
            {
                Field(sb, "SDK", project.Sdk ?? "n/a");
                Field(sb, "TARGET FRAMEWORK(S)", project.TargetFrameworks ?? "n/a");
                Field(sb, "OUTPUT TYPE", project.OutputType ?? "Library (default)");
                Field(sb, "ASSEMBLY NAME", project.AssemblyName ?? project.Name);
                if (!string.IsNullOrEmpty(project.RootNamespace))
                {
                    Field(sb, "ROOT NAMESPACE", project.RootNamespace);
                }
            }

            if (!string.IsNullOrEmpty(project.ParseError))
            {
                Field(sb, "PARSE ERROR", project.ParseError);
            }

            foreach (var group in project.References.GroupBy(r => r.Kind).OrderBy(g => g.Key))
            {
                ListHeader(sb, group.Key switch
                {
                    ProjectReferenceKind.SolutionMember => "PROJECTS IN SOLUTION",
                    ProjectReferenceKind.Project => "PROJECT REFERENCES",
                    ProjectReferenceKind.Package => "NUGET PACKAGES",
                    _ => "ASSEMBLY REFERENCES",
                });
                foreach (var reference in group)
                {
                    var target = reference.TargetPath is null ? string.Empty : $" -> {reference.TargetPath}";
                    var external = group.Key is ProjectReferenceKind.Project or ProjectReferenceKind.SolutionMember && !reference.ExistsInRepository
                        ? " (not in analyzed folder)"
                        : string.Empty;
                    var version = string.IsNullOrEmpty(reference.Version) ? string.Empty : $" {reference.Version}";
                    CodeItem(sb, reference.Name + version, (target + external).Trim().Length == 0 ? null : (target + external).Trim(), 1);
                }
            }
        }

        return sb.ToString();
    }

    public string SymbolIndex(IReadOnlyList<IndexedFile> files, IReadOnlyDictionary<long, IReadOnlyList<CodeSymbol>> symbols)
    {
        var sb = new StringBuilder();
        Section(sb, "SYMBOL INDEX (types)");
        var any = false;
        foreach (var file in files)
        {
            if (!symbols.TryGetValue(file.Id, out var fileSymbols))
            {
                continue;
            }

            var types = fileSymbols.Where(s => s.IsType).ToList();
            if (types.Count == 0)
            {
                continue;
            }

            any = true;
            foreach (var type in types)
            {
                CodeItem(sb, $"{type.Kind.ToExportLabel()} {type.FullName}", $"{file.RelativePath} [lines {type.StartLine}-{type.EndLine}]");
            }
        }

        if (!any)
        {
            Paragraph(sb, "No type declarations were extracted (only C# is analyzed structurally in this version).");
        }

        return sb.ToString();
    }

    public string CrossReferences(IReadOnlyList<IndexedFile> files, IReadOnlyDictionary<long, IReadOnlyList<SymbolRelation>> relations)
    {
        var sb = new StringBuilder();
        Section(sb, "CROSS REFERENCES (syntactic)");
        Paragraph(sb, "Declared base types, constructor dependencies and dependency-injection registrations. Names are not resolved semantically.");
        var any = false;
        foreach (var file in files)
        {
            if (!relations.TryGetValue(file.Id, out var fileRelations))
            {
                continue;
            }

            foreach (var relation in fileRelations.Where(r => r.Kind is RelationKind.BaseType or RelationKind.ConstructorInjection or RelationKind.DiRegistration))
            {
                any = true;
                var label = relation.Kind switch
                {
                    RelationKind.BaseType => "inherits/implements",
                    RelationKind.ConstructorInjection => "depends on",
                    _ => "registered as",
                };
                CodeItem(sb, $"{relation.SourceName} {label} {relation.TargetName}", $"{file.RelativePath}:{relation.Line} ({relation.Evidence})");
            }
        }

        if (!any)
        {
            Paragraph(sb, "No relations were extracted for the included files.");
        }

        return sb.ToString();
    }

    public string SourceFilesTitle()
    {
        var sb = new StringBuilder();
        Section(sb, "SOURCE FILES");
        return sb.ToString();
    }

    public string FileBlock(FileBlockModel model)
    {
        var sb = new StringBuilder();
        var file = model.File;
        FileBanner(sb, file.RelativePath);
        Field(sb, "LANGUAGE", $"{LanguageCatalog.Describe(file.Language).DisplayName} (analysis level: {file.AnalysisLevel})");
        if (!string.IsNullOrEmpty(model.ProjectName))
        {
            Field(sb, "PROJECT", model.ProjectName);
        }

        Field(sb, "LINES", (model.SourceLines?.Count ?? file.LineCount).ToString(Invariant));
        Field(sb, "SIZE", $"{file.SizeBytes.ToString("N0", Invariant)} bytes");
        Field(sb, "SHA256", file.Sha256 ?? "n/a");
        if (model.ModifiedSinceIndex)
        {
            Field(sb, "WARNING", "file modified after the last indexing: metadata and line numbers below may be outdated; the source code is the current content.");
        }

        if (!string.IsNullOrEmpty(file.StatusDetail))
        {
            Field(sb, "ANALYSIS NOTES", file.StatusDetail);
        }

        var namespaces = model.Symbols.Where(s => s.Kind == SymbolKind.Namespace).Select(s => s.FullName).Distinct(StringComparer.Ordinal).ToList();
        if (namespaces.Count > 0)
        {
            Field(sb, "NAMESPACE", string.Join(", ", namespaces));
        }

        var children = model.Symbols.Where(s => s.ParentId is not null).ToLookup(s => s.ParentId!.Value);
        var relationsBySource = model.Relations.Where(r => r.SourceSymbolId is not null).ToLookup(r => r.SourceSymbolId!.Value);

        foreach (var type in model.Symbols.Where(s => s.IsType))
        {
            Field(sb, type.Kind.ToExportLabel(), $"{type.Name} [lines {type.StartLine}-{type.EndLine}]");
            Field(sb, "FULL NAME", type.FullName, 1);
            if (!string.IsNullOrEmpty(type.Accessibility) || !string.IsNullOrEmpty(type.Modifiers))
            {
                Field(sb, "MODIFIERS", $"{type.Accessibility} {type.Modifiers}".Trim(), 1);
            }

            if (!string.IsNullOrEmpty(type.Documentation))
            {
                Field(sb, "SUMMARY", type.Documentation, 1);
            }

            if (!string.IsNullOrEmpty(type.Attributes))
            {
                Field(sb, "ATTRIBUTES", type.Attributes, 1);
            }

            var typeRelations = relationsBySource[type.Id].ToList();
            var bases = typeRelations.Where(r => r.Kind == RelationKind.BaseType).Select(r => r.TargetName).Distinct(StringComparer.Ordinal).ToList();
            if (bases.Count > 0)
            {
                Field(sb, "BASE TYPES", string.Join(", ", bases), 1);
            }

            var dependencies = typeRelations
                .Where(r => r.Kind is RelationKind.ConstructorInjection or RelationKind.MemberType)
                .Select(r => r.TargetName)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (dependencies.Count > 0)
            {
                ListHeader(sb, "DEPENDENCIES", 1);
                foreach (var dependency in dependencies)
                {
                    CodeItem(sb, dependency, null, 2);
                }
            }

            var members = children[type.Id].ToList();
            WriteMembers(sb, "CONSTRUCTORS", members.Where(m => m.Kind is SymbolKind.Constructor or SymbolKind.Destructor));
            WriteMembers(sb, "METHODS", members.Where(m => m.Kind is SymbolKind.Method or SymbolKind.Operator));
            WriteMembers(sb, "PROPERTIES", members.Where(m => m.Kind is SymbolKind.Property or SymbolKind.Indexer));
            WriteMembers(sb, "FIELDS", members.Where(m => m.Kind is SymbolKind.Field or SymbolKind.Constant));
            WriteMembers(sb, "EVENTS", members.Where(m => m.Kind == SymbolKind.Event));
            WriteMembers(sb, "ENUM MEMBERS", members.Where(m => m.Kind == SymbolKind.EnumMember));
        }

        var registrations = model.Relations.Where(r => r.Kind == RelationKind.DiRegistration).ToList();
        if (registrations.Count > 0)
        {
            ListHeader(sb, "DI REGISTRATIONS");
            foreach (var registration in registrations)
            {
                CodeItem(sb, $"{registration.SourceName} -> {registration.TargetName}", $"[line {registration.Line}] ({registration.Evidence})", 1);
            }
        }

        if (model.Redactions.Count > 0)
        {
            ListHeader(sb, "REDACTED VALUES");
            foreach (var redaction in model.Redactions)
            {
                Item(sb, $"line {redaction.Line}: {redaction.Category}", 1);
            }
        }

        if (model.IncludeSource)
        {
            if (model.SourceLines is null)
            {
                Field(sb, "SOURCE CODE", $"NOT AVAILABLE ({model.UnavailableReason ?? "unknown reason"})");
            }
            else
            {
                ListHeader(sb, "SOURCE CODE");
                Code(sb, FormatSource(model.SourceLines, model.IncludeLineNumbers), LanguageCatalog.Describe(file.Language).MarkdownFence);
            }
        }

        FileEnd(sb, file.RelativePath);
        return sb.ToString();
    }

    public string Summary(ExportSummaryModel model)
    {
        var sb = new StringBuilder();
        Section(sb, "EXPORT SUMMARY");
        Field(sb, "FILES EXPORTED", model.FilesExported.ToString("N0", Invariant));
        Field(sb, "CHARACTERS", model.TotalCharacters.ToString("N0", Invariant));
        Field(sb, "ESTIMATED TOKENS", "~" + model.EstimatedTokens.ToString("N0", Invariant));
        Field(sb, "PARTS", model.PartsSoFar.ToString(Invariant));
        Field(sb, "SECRET REDACTION", model.RedactionEnabled ? $"enabled - {model.Redactions.Count} value(s) redacted" : "disabled by explicit user choice");

        if (model.Redactions.Count > 0)
        {
            ListHeader(sb, "REDACTED VALUES (path:line category)");
            foreach (var redaction in model.Redactions.Take(1000))
            {
                Item(sb, $"{redaction.RelativePath}:{redaction.Line} {redaction.Category}", 1);
            }

            if (model.Redactions.Count > 1000)
            {
                Item(sb, $"... {model.Redactions.Count - 1000} more", 1);
            }
        }

        if (model.ExcludedSensitiveFiles.Count > 0)
        {
            ListHeader(sb, "SENSITIVE FILES EXCLUDED");
            foreach (var path in model.ExcludedSensitiveFiles)
            {
                Item(sb, path, 1);
            }
        }

        if (model.ModifiedSinceIndex.Count > 0)
        {
            ListHeader(sb, "FILES MODIFIED AFTER INDEXING (re-index to refresh metadata)");
            foreach (var path in model.ModifiedSinceIndex)
            {
                Item(sb, path, 1);
            }
        }

        if (model.Warnings.Count > 0)
        {
            ListHeader(sb, "WARNINGS");
            foreach (var warning in model.Warnings)
            {
                Item(sb, warning, 1);
            }
        }

        Section(sb, "ANALYSIS LIMITS");
        Item(sb, "C#: syntactic analysis only. Relations (base types, dependencies, invocations) are name-based and not resolved to definitions.");
        Item(sb, "T-SQL and all other languages: indexed as text only in this version; no SQL object or dependency extraction yet.");
        Item(sb, "Solutions and project files are read structurally (no MSBuild evaluation of conditions, imports or properties).");
        Item(sb, "Binary files, files above the size limit and unreadable files are not included.");
        Item(sb, "Anything not shown here was not verified by the tool and must not be assumed.");
        Paragraph(sb, "END OF EXPORT");
        return sb.ToString();
    }

    public string IndexDocument(ExportIndexModel model)
    {
        var sb = new StringBuilder();
        var header = model.Header;
        Title(sb, "LOCAL CODE INTELLIGENCE ANALYZER - EXPORT INDEX (MASTER FILE)");
        Field(sb, "GENERATED (UTC)", header.GeneratedUtc.ToString("yyyy-MM-dd HH:mm:ss", Invariant));
        Field(sb, "REPOSITORY", header.Repository.Name);
        Field(sb, "GIT BRANCH", header.Repository.GitBranch ?? "n/a");
        Field(sb, "GIT COMMIT", header.Repository.GitCommit ?? "n/a");
        Field(sb, "EXPORT NAME", header.BaseName);
        Field(sb, "PARTS", model.Parts.Count.ToString(Invariant));
        Field(sb, "FILES", header.FileCount.ToString("N0", Invariant));
        Field(sb, "TOTAL ESTIMATED TOKENS", "~" + model.Parts.Sum(p => p.EstimatedTokens).ToString("N0", Invariant));
        Section(sb, "READING ORDER");
        Paragraph(sb, "Read or provide the parts in numerical order. Part 1 contains the document header, directory tree, file list, projects, symbol index and cross references; the following parts contain the source files. A file split across parts is marked with CONTINUES/CONTINUED notices. The export summary is at the end of the last part.");
        Section(sb, "PARTS");
        foreach (var part in model.Parts)
        {
            SubSection(sb, $"PART {part.Number:000}: {Path.GetFileName(part.FilePath)}");
            Field(sb, "CHARACTERS", part.Characters.ToString("N0", Invariant));
            Field(sb, "ESTIMATED TOKENS", "~" + part.EstimatedTokens.ToString("N0", Invariant));
            if (part.Files.Count > 0)
            {
                ListHeader(sb, "FILES");
                foreach (var path in part.Files)
                {
                    Item(sb, path, 1);
                }
            }
        }

        sb.Append(DirectoryTree(model.DirectoryPaths));
        return sb.ToString();
    }

    // ------------------------------------------------------------------ helper

    private void WriteMembers(StringBuilder sb, string title, IEnumerable<CodeSymbol> members)
    {
        var list = members.ToList();
        if (list.Count == 0)
        {
            return;
        }

        ListHeader(sb, title, 1);
        foreach (var member in list)
        {
            CodeItem(sb, member.Signature ?? member.Name, $"[lines {member.StartLine}-{member.EndLine}]", 2);
        }
    }

    protected static IReadOnlyList<string> FormatSource(IReadOnlyList<string> lines, bool includeLineNumbers)
    {
        if (!includeLineNumbers)
        {
            return lines;
        }

        var width = Math.Max(3, lines.Count.ToString(Invariant).Length);
        var result = new string[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            result[i] = (i + 1).ToString(Invariant).PadLeft(width) + " | " + lines[i];
        }

        return result;
    }

    private static void RenderTree(TreeNode node, string indent, List<string> lines)
    {
        var children = node.Children
            .OrderBy(c => c.Children.Count == 0)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        for (var i = 0; i < children.Count; i++)
        {
            var last = i == children.Count - 1;
            var child = children[i];
            lines.Add(indent + (last ? "└── " : "├── ") + child.Name + (child.Children.Count > 0 ? "/" : string.Empty));
            RenderTree(child, indent + (last ? "    " : "│   "), lines);
        }
    }

    private sealed class TreeNode(string name)
    {
        private readonly Dictionary<string, TreeNode> _children = new(StringComparer.OrdinalIgnoreCase);

        public string Name { get; } = name;

        public ICollection<TreeNode> Children => _children.Values;

        public TreeNode GetOrAdd(string childName)
        {
            if (!_children.TryGetValue(childName, out var child))
            {
                child = new TreeNode(childName);
                _children[childName] = child;
            }

            return child;
        }
    }
}
