using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Lcia.Application;
using DocumentList = System.Windows.Documents.List;

namespace Lcia.App.Views;

/// <summary>
/// Converte i blocchi Markdown della guida in un FlowDocument. I colori sono riferimenti alle risorse
/// del tema (DynamicResource), quindi il documento segue il tema chiaro/scuro senza essere ricostruito.
/// </summary>
internal static class HelpDocumentBuilder
{
    private static readonly FontFamily CodeFont = new("Cascadia Mono, Consolas, Courier New");
    private static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");

    public static FlowDocument Build(IReadOnlyList<MarkdownBlock> blocks, IReadOnlyList<string> highlightTerms)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(28, 18, 28, 28),
            FontFamily = TextFont,
            FontSize = 14,
            LineHeight = 22,
            TextAlignment = TextAlignment.Left,
            Background = Brushes.Transparent,
            IsHyphenationEnabled = false,
        };
        document.SetResourceReference(FlowDocument.ForegroundProperty, "Lcia.Text");

        foreach (var block in blocks)
        {
            var element = CreateBlock(block, highlightTerms);
            if (element is not null)
            {
                document.Blocks.Add(element);
            }
        }

        return document;
    }

    private static Block? CreateBlock(MarkdownBlock block, IReadOnlyList<string> terms)
    {
        switch (block.Kind)
        {
            case MarkdownBlockKind.Heading:
            {
                var paragraph = new Paragraph
                {
                    FontSize = block.Level switch { 1 => 26, 2 => 21, 3 => 17, _ => 15 },
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, block.Level <= 1 ? 0 : 18, 0, 8),
                    LineHeight = block.Level switch { 1 => 32, 2 => 28, _ => 24 },
                };
                AddInlines(paragraph.Inlines, block.Text, terms);
                return paragraph;
            }

            case MarkdownBlockKind.Paragraph:
            {
                var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 10) };
                AddInlines(paragraph.Inlines, block.Text, terms);
                return paragraph;
            }

            case MarkdownBlockKind.Quote:
            {
                var paragraph = new Paragraph
                {
                    Margin = new Thickness(0, 0, 0, 12),
                    Padding = new Thickness(12, 6, 12, 6),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                };
                paragraph.SetResourceReference(Block.BorderBrushProperty, "Lcia.Accent");
                paragraph.SetResourceReference(TextElement.BackgroundProperty, "Lcia.InfoBackground");
                AddInlines(paragraph.Inlines, block.Text, terms);
                return paragraph;
            }

            case MarkdownBlockKind.BulletList:
            case MarkdownBlockKind.NumberedList:
            {
                var list = new DocumentList
                {
                    MarkerStyle = block.Kind == MarkdownBlockKind.NumberedList ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                    Margin = new Thickness(0, 0, 0, 10),
                    Padding = new Thickness(22, 0, 0, 0),
                };
                foreach (var item in block.Items)
                {
                    var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
                    AddInlines(paragraph.Inlines, item, terms);
                    list.ListItems.Add(new ListItem(paragraph));
                }

                return list;
            }

            case MarkdownBlockKind.Code:
            {
                var paragraph = new Paragraph
                {
                    FontFamily = CodeFont,
                    FontSize = 13,
                    LineHeight = 19,
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 12),
                };
                paragraph.SetResourceReference(TextElement.BackgroundProperty, "Lcia.InlineCodeBackground");
                for (var i = 0; i < block.Lines.Count; i++)
                {
                    if (i > 0)
                    {
                        paragraph.Inlines.Add(new LineBreak());
                    }

                    AddText(paragraph.Inlines, block.Lines[i], terms, null);
                }

                return paragraph;
            }

            case MarkdownBlockKind.Table:
                return CreateTable(block, terms);

            case MarkdownBlockKind.Rule:
            {
                var paragraph = new Paragraph { Margin = new Thickness(0, 6, 0, 14), BorderThickness = new Thickness(0, 0, 0, 1), FontSize = 2 };
                paragraph.SetResourceReference(Block.BorderBrushProperty, "Lcia.Divider");
                return paragraph;
            }

            default:
                return null;
        }
    }

    private static Table CreateTable(MarkdownBlock block, IReadOnlyList<string> terms)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 14), BorderThickness = new Thickness(1, 1, 0, 0) };
        table.SetResourceReference(Block.BorderBrushProperty, "Lcia.CardBorder");
        var columns = block.Rows.Count == 0 ? 0 : block.Rows.Max(r => r.Count);
        for (var c = 0; c < columns; c++)
        {
            var longest = block.Rows.Select(r => c < r.Count ? MarkdownLite.ToPlainText(r[c]).Length : 0).DefaultIfEmpty(0).Max();
            table.Columns.Add(new TableColumn { Width = new GridLength(Math.Clamp(longest, 8, 60), GridUnitType.Star) });
        }

        var group = new TableRowGroup();
        for (var r = 0; r < block.Rows.Count; r++)
        {
            var row = new TableRow();
            var header = r == 0;
            if (header)
            {
                row.SetResourceReference(TextElement.BackgroundProperty, "Lcia.Surface");
            }

            for (var c = 0; c < columns; c++)
            {
                var text = c < block.Rows[r].Count ? block.Rows[r][c] : string.Empty;
                var paragraph = new Paragraph { Margin = new Thickness(0) };
                if (header)
                {
                    paragraph.FontWeight = FontWeights.SemiBold;
                }

                AddInlines(paragraph.Inlines, text, terms);
                var cell = new TableCell(paragraph) { Padding = new Thickness(8, 5, 8, 5), BorderThickness = new Thickness(0, 0, 1, 1) };
                cell.SetResourceReference(TableCell.BorderBrushProperty, "Lcia.CardBorder");
                row.Cells.Add(cell);
            }

            group.Rows.Add(row);
        }

        table.RowGroups.Add(group);
        return table;
    }

    private static void AddInlines(InlineCollection target, string markdown, IReadOnlyList<string> terms)
    {
        foreach (var inline in MarkdownLite.ParseInline(markdown))
        {
            switch (inline.Kind)
            {
                case MarkdownInlineKind.Bold:
                    AddText(target, inline.Text, terms, run => run.FontWeight = FontWeights.SemiBold);
                    break;
                case MarkdownInlineKind.Italic:
                    AddText(target, inline.Text, terms, run => run.FontStyle = FontStyles.Italic);
                    break;
                case MarkdownInlineKind.Code:
                    AddText(target, inline.Text, terms, run =>
                    {
                        run.FontFamily = CodeFont;
                        run.FontSize = 13;
                        run.SetResourceReference(TextElement.BackgroundProperty, "Lcia.InlineCodeBackground");
                    });
                    break;
                case MarkdownInlineKind.Link:
                    target.Add(CreateLink(inline));
                    break;
                default:
                    AddText(target, inline.Text, terms, null);
                    break;
            }
        }
    }

    private static Inline CreateLink(MarkdownInline inline)
    {
        if (Uri.TryCreate(inline.Url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            var link = new Hyperlink(new Run(inline.Text)) { NavigateUri = uri, ToolTip = uri.AbsoluteUri };
            link.SetResourceReference(TextElement.ForegroundProperty, "Lcia.Accent");
            link.RequestNavigate += (_, e) =>
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            };
            return link;
        }

        // Collegamenti relativi (altri documenti del progetto): solo testo.
        return new Run(inline.Text) { FontWeight = FontWeights.SemiBold };
    }

    /// <summary>Aggiunge testo evidenziando le parole cercate.</summary>
    private static void AddText(InlineCollection target, string text, IReadOnlyList<string> terms, Action<Run>? style)
    {
        var position = 0;
        while (position < text.Length)
        {
            var (index, length) = FindNext(text, position, terms);
            if (index < 0)
            {
                target.Add(MakeRun(text[position..], style, highlighted: false));
                return;
            }

            if (index > position)
            {
                target.Add(MakeRun(text[position..index], style, highlighted: false));
            }

            target.Add(MakeRun(text.Substring(index, length), style, highlighted: true));
            position = index + length;
        }
    }

    private static Run MakeRun(string text, Action<Run>? style, bool highlighted)
    {
        var run = new Run(text);
        style?.Invoke(run);
        if (highlighted)
        {
            run.SetResourceReference(TextElement.BackgroundProperty, "Lcia.SearchHighlight");
        }

        return run;
    }

    private static (int Index, int Length) FindNext(string text, int start, IReadOnlyList<string> terms)
    {
        var bestIndex = -1;
        var bestLength = 0;
        foreach (var term in terms)
        {
            var index = text.IndexOf(term, start, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && (bestIndex < 0 || index < bestIndex || (index == bestIndex && term.Length > bestLength)))
            {
                bestIndex = index;
                bestLength = term.Length;
            }
        }

        return (bestIndex, bestLength);
    }
}
