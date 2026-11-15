using System.Text;

namespace Lcia.Export;

/// <summary>Rendering Markdown (CommonMark/GitHub): titoli, elenchi, tabelle, blocchi di codice con linguaggio.</summary>
internal sealed class MarkdownExportFormatter : ExportFormatter
{
    public override string Extension => ".md";

    protected override void Title(StringBuilder sb, string text)
    {
        sb.Append("# ").AppendLine(Escape(text)).AppendLine();
    }

    protected override void Section(StringBuilder sb, string text)
    {
        sb.AppendLine().Append("## ").AppendLine(Escape(text)).AppendLine();
    }

    protected override void SubSection(StringBuilder sb, string text)
    {
        sb.AppendLine().Append("### ").AppendLine(Escape(text)).AppendLine();
    }

    protected override void Field(StringBuilder sb, string key, string value, int indent = 0)
    {
        sb.Append(Indent(indent)).Append("- **").Append(Escape(key)).Append(":** ").AppendLine(Escape(value));
    }

    protected override void ListHeader(StringBuilder sb, string key, int indent = 0)
    {
        sb.Append(Indent(indent)).Append("- **").Append(Escape(key)).AppendLine(":**");
    }

    protected override void Item(StringBuilder sb, string text, int indent = 0)
    {
        sb.Append(Indent(indent)).Append("- ").AppendLine(Escape(text));
    }

    protected override void CodeItem(StringBuilder sb, string code, string? suffix, int indent = 0)
    {
        sb.Append(Indent(indent)).Append("- ").Append(InlineCode(code));
        if (!string.IsNullOrEmpty(suffix))
        {
            sb.Append(' ').Append(Escape(suffix));
        }

        sb.AppendLine();
    }

    protected override void Paragraph(StringBuilder sb, string text)
    {
        sb.AppendLine().AppendLine(Escape(text));
    }

    protected override void Code(StringBuilder sb, IReadOnlyList<string> lines, string language)
    {
        var fence = new string('`', Math.Max(3, LongestBacktickRun(lines) + 1));
        sb.AppendLine().Append(fence).AppendLine(language);
        foreach (var line in lines)
        {
            sb.AppendLine(line);
        }

        sb.AppendLine(fence);
    }

    protected override void FileBanner(StringBuilder sb, string relativePath)
    {
        sb.AppendLine().AppendLine("---").AppendLine().Append("## FILE: ").AppendLine(InlineCode(relativePath)).AppendLine();
    }

    protected override void FileEnd(StringBuilder sb, string relativePath)
    {
        sb.AppendLine().Append("*END OF FILE: ").Append(InlineCode(relativePath)).AppendLine("*");
    }

    protected override void Table(StringBuilder sb, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        sb.AppendLine().Append("| ").Append(string.Join(" | ", headers.Select(Cell))).AppendLine(" |");
        sb.Append('|').Append(string.Join("|", headers.Select(_ => "---"))).AppendLine("|");
        foreach (var row in rows)
        {
            sb.Append("| ").Append(string.Join(" | ", row.Select(Cell))).AppendLine(" |");
        }
    }

    public override string PartHeader(int partNumber, string baseName, string indexFileName) =>
        $"# Export \"{Escape(baseName)}\" - part {partNumber:000}{Environment.NewLine}{Environment.NewLine}" +
        $"Read the parts in numerical order. Index and reading guide: {InlineCode(indexFileName)}{Environment.NewLine}";

    public override string ContinuationNotice(string relativePath, int nextPart) =>
        $"{Environment.NewLine}> **CONTINUES IN PART {nextPart:000}:** {InlineCode(relativePath)}{Environment.NewLine}";

    public override string ContinuedNotice(string relativePath, int previousPart) =>
        $"> **CONTINUED FROM PART {previousPart:000}:** {InlineCode(relativePath)}{Environment.NewLine}{Environment.NewLine}";

    private static string Indent(int level) => level <= 0 ? string.Empty : new string(' ', level * 2);

    private static string InlineCode(string code)
    {
        var fence = code.Contains('`', StringComparison.Ordinal) ? "``" : "`";
        var padding = code.StartsWith('`') || code.EndsWith('`') ? " " : string.Empty;
        return fence + padding + code + padding + fence;
    }

    /// <summary>
    /// Evita che &lt;T&gt; e simili vengano interpretati come HTML: il testo con parentesi angolari
    /// viene reso come codice inline, così resta leggibile sia "grezzo" (per le AI) sia renderizzato.
    /// </summary>
    private static string Escape(string text) =>
        text.Contains('<', StringComparison.Ordinal) || text.Contains('>', StringComparison.Ordinal) ? InlineCode(text) : text;

    private static string Cell(string text) => Escape(text).Replace("|", "\\|", StringComparison.Ordinal);

    private static int LongestBacktickRun(IReadOnlyList<string> lines)
    {
        var longest = 0;
        foreach (var line in lines)
        {
            var current = 0;
            foreach (var c in line)
            {
                current = c == '`' ? current + 1 : 0;
                longest = Math.Max(longest, current);
            }
        }

        return longest;
    }
}
