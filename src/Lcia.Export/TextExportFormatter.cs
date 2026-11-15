using System.Text;

namespace Lcia.Export;

/// <summary>Rendering in testo semplice (UTF-8), con i delimitatori di file del formato richiesto.</summary>
internal sealed class TextExportFormatter : ExportFormatter
{
    private const string HeavyRule = "##################################################";
    private const string FileRule = "==================================================";
    private const string SectionRule = "--------------------------------------------------";

    public override string Extension => ".txt";

    protected override void Title(StringBuilder sb, string text)
    {
        sb.AppendLine(HeavyRule).AppendLine(text).AppendLine(HeavyRule).AppendLine();
    }

    protected override void Section(StringBuilder sb, string text)
    {
        sb.AppendLine().AppendLine(SectionRule).AppendLine(text).AppendLine(SectionRule);
    }

    protected override void SubSection(StringBuilder sb, string text)
    {
        sb.AppendLine().Append('[').Append(text).AppendLine("]");
    }

    protected override void Field(StringBuilder sb, string key, string value, int indent = 0)
    {
        sb.Append(Indent(indent)).Append(key).Append(": ").AppendLine(value);
    }

    protected override void ListHeader(StringBuilder sb, string key, int indent = 0)
    {
        sb.Append(Indent(indent)).Append(key).AppendLine(":");
    }

    protected override void Item(StringBuilder sb, string text, int indent = 0)
    {
        sb.Append(Indent(indent)).Append("- ").AppendLine(text);
    }

    protected override void CodeItem(StringBuilder sb, string code, string? suffix, int indent = 0)
    {
        sb.Append(Indent(indent)).Append("- ").Append(code);
        if (!string.IsNullOrEmpty(suffix))
        {
            sb.Append(' ').Append(suffix);
        }

        sb.AppendLine();
    }

    protected override void Paragraph(StringBuilder sb, string text)
    {
        sb.AppendLine(text);
    }

    protected override void Code(StringBuilder sb, IReadOnlyList<string> lines, string language)
    {
        if (lines.Count == 0)
        {
            sb.AppendLine("(empty file)");
            return;
        }

        foreach (var line in lines)
        {
            sb.AppendLine(line);
        }
    }

    protected override void FileBanner(StringBuilder sb, string relativePath)
    {
        sb.AppendLine().AppendLine(FileRule).Append("FILE: ").AppendLine(relativePath).AppendLine(FileRule);
    }

    protected override void FileEnd(StringBuilder sb, string relativePath)
    {
        sb.Append("END OF FILE: ").AppendLine(relativePath);
    }

    protected override void Table(StringBuilder sb, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        sb.AppendLine(string.Join(" | ", headers));
        sb.AppendLine(string.Join("-+-", headers.Select(h => new string('-', Math.Max(1, h.Length)))));
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(" | ", row));
        }
    }

    public override string PartHeader(int partNumber, string baseName, string indexFileName)
    {
        var sb = new StringBuilder();
        sb.AppendLine(HeavyRule)
            .AppendLine($"LOCAL CODE INTELLIGENCE ANALYZER - EXPORT \"{baseName}\" - PART {partNumber:000}")
            .AppendLine($"Read the parts in numerical order. Index and reading guide: {indexFileName}")
            .AppendLine(HeavyRule);
        return sb.ToString();
    }

    public override string ContinuationNotice(string relativePath, int nextPart) =>
        $"{Environment.NewLine}[CONTINUES IN PART {nextPart:000}: {relativePath}]{Environment.NewLine}";

    public override string ContinuedNotice(string relativePath, int previousPart) =>
        $"[CONTINUED FROM PART {previousPart:000}: {relativePath}]{Environment.NewLine}";

    private static string Indent(int level) => level <= 0 ? string.Empty : new string(' ', level * 2);
}
