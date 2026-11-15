using System.Text;

namespace Lcia.Application;

/// <summary>Tipi di blocco del Markdown supportato dalla guida in linea.</summary>
public enum MarkdownBlockKind
{
    Heading,
    Paragraph,
    BulletList,
    NumberedList,
    Code,
    Table,
    Quote,
    Rule,
}

/// <summary>
/// Blocco di un documento Markdown. A seconda del tipo si usano: <see cref="Text"/> (titolo, paragrafo,
/// citazione), <see cref="Items"/> (elenchi), <see cref="Lines"/> (codice), <see cref="Rows"/> (tabella,
/// la prima riga è l'intestazione).
/// </summary>
public sealed record MarkdownBlock(MarkdownBlockKind Kind)
{
    public int Level { get; init; }

    public string Text { get; init; } = string.Empty;

    public string Language { get; init; } = string.Empty;

    public IReadOnlyList<string> Items { get; init; } = [];

    public IReadOnlyList<string> Lines { get; init; } = [];

    public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = [];

    /// <summary>Testo semplice del blocco (per la ricerca nella guida).</summary>
    public string PlainText => Kind switch
    {
        MarkdownBlockKind.BulletList or MarkdownBlockKind.NumberedList => string.Join(' ', Items.Select(MarkdownLite.ToPlainText)),
        MarkdownBlockKind.Code => string.Join(' ', Lines),
        MarkdownBlockKind.Table => string.Join(' ', Rows.SelectMany(r => r).Select(MarkdownLite.ToPlainText)),
        _ => MarkdownLite.ToPlainText(Text),
    };
}

public enum MarkdownInlineKind
{
    Text,
    Bold,
    Italic,
    Code,
    Link,
}

/// <summary>Porzione di testo con formattazione in linea.</summary>
public sealed record MarkdownInline(MarkdownInlineKind Kind, string Text, string? Url = null);

/// <summary>
/// Parser Markdown minimale e prevedibile per la guida in linea: titoli (#), paragrafi, elenchi puntati
/// e numerati (con righe di continuazione rientrate), blocchi di codice ```, tabelle con barre verticali,
/// citazioni (&gt;), righe orizzontali; in linea: **grassetto**, *corsivo*, `codice`, [link](url).
/// Non interpreta HTML.
/// </summary>
public static class MarkdownLite
{
    public static IReadOnlyList<MarkdownBlock> Parse(string? markdown)
    {
        var blocks = new List<MarkdownBlock>();
        var lines = TextLines.Split(markdown ?? string.Empty);
        var paragraph = new List<string>();
        var i = 0;

        void FlushParagraph()
        {
            if (paragraph.Count > 0)
            {
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Paragraph) { Text = string.Join(' ', paragraph) });
                paragraph.Clear();
            }
        }

        while (i < lines.Count)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                i++;
                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                var language = trimmed[3..].Trim();
                var code = new List<string>();
                i++;
                while (i < lines.Count && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    code.Add(lines[i]);
                    i++;
                }

                i++; // chiusura del blocco (o fine del documento)
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Code) { Language = language, Lines = code });
                continue;
            }

            var level = HeadingLevel(trimmed);
            if (level > 0)
            {
                FlushParagraph();
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Heading) { Level = level, Text = trimmed[level..].Trim().TrimEnd('#').Trim() });
                i++;
                continue;
            }

            if (trimmed is "---" or "***" or "___")
            {
                FlushParagraph();
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Rule));
                i++;
                continue;
            }

            if (trimmed.StartsWith('|') && i + 1 < lines.Count && IsTableSeparator(lines[i + 1]))
            {
                FlushParagraph();
                var rows = new List<IReadOnlyList<string>> { SplitTableRow(trimmed) };
                i += 2;
                while (i < lines.Count && lines[i].Trim().StartsWith('|'))
                {
                    rows.Add(SplitTableRow(lines[i].Trim()));
                    i++;
                }

                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Table) { Rows = rows });
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                FlushParagraph();
                var quote = new List<string>();
                while (i < lines.Count && lines[i].Trim().StartsWith('>'))
                {
                    quote.Add(lines[i].Trim()[1..].Trim());
                    i++;
                }

                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Quote) { Text = string.Join(' ', quote.Where(q => q.Length > 0)) });
                continue;
            }

            if (ListMarker(line, out var ordered, out _) > 0)
            {
                FlushParagraph();
                var items = new List<string>();
                var kind = ordered ? MarkdownBlockKind.NumberedList : MarkdownBlockKind.BulletList;
                while (i < lines.Count)
                {
                    var current = lines[i];
                    var markerLength = ListMarker(current, out var currentOrdered, out var indent);
                    if (markerLength > 0 && indent < 2 && currentOrdered == ordered)
                    {
                        items.Add(current.Trim()[markerLength..].Trim());
                        i++;
                        continue;
                    }

                    // Riga rientrata: continuazione dell'elemento precedente (anche sotto-elenchi, resi in linea).
                    if (items.Count > 0 && current.Trim().Length > 0 && (current.StartsWith("  ", StringComparison.Ordinal) || current.StartsWith('\t')))
                    {
                        var continuation = current.Trim();
                        var nested = ListMarker(continuation, out _, out _);
                        items[^1] += (nested > 0 ? " · " + continuation[nested..].Trim() : " " + continuation);
                        i++;
                        continue;
                    }

                    break;
                }

                blocks.Add(new MarkdownBlock(kind) { Items = items });
                continue;
            }

            paragraph.Add(trimmed);
            i++;
        }

        FlushParagraph();
        return blocks;
    }

    /// <summary>Divide il testo di un blocco nelle porzioni in linea (grassetto, corsivo, codice, link).</summary>
    public static IReadOnlyList<MarkdownInline> ParseInline(string? text)
    {
        var result = new List<MarkdownInline>();
        var value = text ?? string.Empty;
        var plain = new StringBuilder();
        var i = 0;

        void FlushPlain()
        {
            if (plain.Length > 0)
            {
                result.Add(new MarkdownInline(MarkdownInlineKind.Text, plain.ToString()));
                plain.Clear();
            }
        }

        while (i < value.Length)
        {
            var c = value[i];

            if (c == '\\' && i + 1 < value.Length && "\\`*_[]()#|>".Contains(value[i + 1]))
            {
                plain.Append(value[i + 1]);
                i += 2;
                continue;
            }

            if (c == '`')
            {
                var end = value.IndexOf('`', i + 1);
                if (end > i)
                {
                    FlushPlain();
                    result.Add(new MarkdownInline(MarkdownInlineKind.Code, value[(i + 1)..end]));
                    i = end + 1;
                    continue;
                }
            }

            if (c == '*' && i + 1 < value.Length && value[i + 1] == '*')
            {
                var end = value.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > i + 2)
                {
                    FlushPlain();
                    result.Add(new MarkdownInline(MarkdownInlineKind.Bold, ToPlainText(value[(i + 2)..end])));
                    i = end + 2;
                    continue;
                }
            }

            if (c == '*' && i + 1 < value.Length && value[i + 1] != ' ' && value[i + 1] != '*')
            {
                var end = value.IndexOf('*', i + 1);
                if (end > i + 1 && value[end - 1] != ' ')
                {
                    FlushPlain();
                    result.Add(new MarkdownInline(MarkdownInlineKind.Italic, value[(i + 1)..end]));
                    i = end + 1;
                    continue;
                }
            }

            if (c == '[')
            {
                var close = value.IndexOf(']', i + 1);
                if (close > i && close + 1 < value.Length && value[close + 1] == '(')
                {
                    var urlEnd = value.IndexOf(')', close + 2);
                    if (urlEnd > close)
                    {
                        FlushPlain();
                        result.Add(new MarkdownInline(MarkdownInlineKind.Link, ToPlainText(value[(i + 1)..close]), value[(close + 2)..urlEnd].Trim()));
                        i = urlEnd + 1;
                        continue;
                    }
                }
            }

            plain.Append(c);
            i++;
        }

        FlushPlain();
        return result;
    }

    /// <summary>Testo senza marcatori in linea.</summary>
    public static string ToPlainText(string? text)
    {
        var builder = new StringBuilder();
        foreach (var inline in ParseInline(text))
        {
            builder.Append(inline.Text);
        }

        return builder.ToString();
    }

    private static int HeadingLevel(string trimmed)
    {
        var level = 0;
        while (level < trimmed.Length && level < 6 && trimmed[level] == '#')
        {
            level++;
        }

        return level > 0 && level < trimmed.Length && trimmed[level] == ' ' ? level : 0;
    }

    /// <summary>Lunghezza del marcatore di elenco ("- ", "* ", "12. ") nella riga senza rientro, 0 se assente.</summary>
    private static int ListMarker(string line, out bool ordered, out int indent)
    {
        ordered = false;
        indent = line.Length - line.TrimStart().Length;
        var trimmed = line.TrimStart();
        if (trimmed.Length >= 2 && (trimmed[0] == '-' || trimmed[0] == '*' || trimmed[0] == '+') && trimmed[1] == ' ')
        {
            return 2;
        }

        var digits = 0;
        while (digits < trimmed.Length && char.IsAsciiDigit(trimmed[digits]))
        {
            digits++;
        }

        if (digits > 0 && digits < 4 && digits + 1 < trimmed.Length && (trimmed[digits] == '.' || trimmed[digits] == ')') && trimmed[digits + 1] == ' ')
        {
            ordered = true;
            return digits + 2;
        }

        return 0;
    }

    private static bool IsTableSeparator(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith('|') || !trimmed.Contains('-'))
        {
            return false;
        }

        return trimmed.All(ch => ch is '|' or '-' or ':' or ' ');
    }

    private static List<string> SplitTableRow(string row)
    {
        var content = row.Trim();
        if (content.StartsWith('|'))
        {
            content = content[1..];
        }

        if (content.EndsWith('|') && !content.EndsWith("\\|", StringComparison.Ordinal))
        {
            content = content[..^1];
        }

        var cells = new List<string>();
        var current = new StringBuilder();
        var inCode = false;
        for (var i = 0; i < content.Length; i++)
        {
            var ch = content[i];
            if (ch == '\\' && i + 1 < content.Length && content[i + 1] == '|')
            {
                current.Append('|');
                i++;
                continue;
            }

            if (ch == '`')
            {
                inCode = !inCode;
            }

            if (ch == '|' && !inCode)
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        cells.Add(current.ToString().Trim());
        return cells;
    }
}
