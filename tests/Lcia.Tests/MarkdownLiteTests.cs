using Lcia.Application;

namespace Lcia.Tests;

public sealed class MarkdownLiteTests
{
    [Fact]
    public void ParsesHeadingsParagraphsListsCodeAndTables()
    {
        const string markdown = """
            # Titolo

            Primo paragrafo
            su due righe.

            ## Sezione

            - uno
            - due
              continua
            1. primo
            2. secondo

            ```powershell
            lcia index C:\src
              rientro
            ```

            | Colonna A | Colonna B |
            |---|---|
            | `a|b` | testo |
            > nota
            ---
            """;

        var blocks = MarkdownLite.Parse(markdown);
        var kinds = blocks.Select(b => b.Kind).ToArray();

        Assert.Equal(
            new[]
            {
                MarkdownBlockKind.Heading, MarkdownBlockKind.Paragraph, MarkdownBlockKind.Heading, MarkdownBlockKind.BulletList,
                MarkdownBlockKind.NumberedList, MarkdownBlockKind.Code, MarkdownBlockKind.Table, MarkdownBlockKind.Quote, MarkdownBlockKind.Rule,
            },
            kinds);
        Assert.Equal(1, blocks[0].Level);
        Assert.Equal("Titolo", blocks[0].Text);
        Assert.Equal("Primo paragrafo su due righe.", blocks[1].Text);
        Assert.Equal(2, blocks[2].Level);
        Assert.Equal(new[] { "uno", "due continua" }, blocks[3].Items);
        Assert.Equal(new[] { "primo", "secondo" }, blocks[4].Items);
        Assert.Equal("powershell", blocks[5].Language);
        Assert.Equal(new[] { @"lcia index C:\src", "  rientro" }, blocks[5].Lines);
        Assert.Equal(new[] { "Colonna A", "Colonna B" }, blocks[6].Rows[0]);
        Assert.Equal(new[] { "`a|b`", "testo" }, blocks[6].Rows[1]);
        Assert.Equal("nota", blocks[7].Text);
    }

    [Fact]
    public void ParsesInlineFormatting()
    {
        var inlines = MarkdownLite.ParseInline("Premi **Esporta**, usa `lcia --help`, *poi* [leggi](https://example.com/guida) e \\*non\\* altro.");

        Assert.Contains(inlines, i => i.Kind == MarkdownInlineKind.Bold && i.Text == "Esporta");
        Assert.Contains(inlines, i => i.Kind == MarkdownInlineKind.Code && i.Text == "lcia --help");
        Assert.Contains(inlines, i => i.Kind == MarkdownInlineKind.Italic && i.Text == "poi");
        Assert.Contains(inlines, i => i.Kind == MarkdownInlineKind.Link && i.Text == "leggi" && i.Url == "https://example.com/guida");
        Assert.Equal("Premi Esporta, usa lcia --help, poi leggi e *non* altro.", MarkdownLite.ToPlainText("Premi **Esporta**, usa `lcia --help`, *poi* [leggi](https://example.com/guida) e \\*non\\* altro."));
    }

    [Fact]
    public void KeepsAsterisksThatAreNotFormatting()
    {
        Assert.Equal("2 * 3 = 6", MarkdownLite.ToPlainText("2 * 3 = 6"));
        Assert.Equal("Cerca *Repository", MarkdownLite.ToPlainText("Cerca *Repository"));
    }

    [Fact]
    public void UnclosedCodeFenceKeepsTheRestAsCode()
    {
        var blocks = MarkdownLite.Parse("Testo\n\n```\nriga 1\nriga 2");
        Assert.Equal(MarkdownBlockKind.Code, blocks[^1].Kind);
        Assert.Equal(new[] { "riga 1", "riga 2" }, blocks[^1].Lines);
    }

    [Fact]
    public void UserGuideHasTheSectionsShownInTheHelpPage()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "docs", "USER_GUIDE.md");
        var blocks = MarkdownLite.Parse(File.ReadAllText(path));
        var headings = blocks.Where(b => b.Kind == MarkdownBlockKind.Heading).Select(b => MarkdownLite.ToPlainText(b.Text)).ToList();

        Assert.Equal(MarkdownBlockKind.Heading, blocks[0].Kind);
        Assert.Equal(1, blocks[0].Level);
        Assert.Contains(headings, h => h.Contains("Primo avvio", StringComparison.Ordinal));
        Assert.Contains(headings, h => h.Contains("Scorciatoie", StringComparison.Ordinal));
        Assert.Contains(headings, h => h.Contains("Tema", StringComparison.Ordinal));
        Assert.Contains(blocks, b => b.Kind == MarkdownBlockKind.Table);
        Assert.Contains(blocks, b => b.Kind == MarkdownBlockKind.Code);
        Assert.DoesNotContain(blocks, b => b.Kind == MarkdownBlockKind.Paragraph && b.Text.StartsWith('|'));
        Assert.DoesNotContain(blocks, b => b.Kind == MarkdownBlockKind.Paragraph && b.Text.StartsWith("```", StringComparison.Ordinal));
    }
}
