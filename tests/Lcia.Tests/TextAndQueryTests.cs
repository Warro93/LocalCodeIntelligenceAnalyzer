using System.Text;
using Lcia.Application;

namespace Lcia.Tests;

public sealed class TextLinesTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("abc", 1)]
    [InlineData("a\nb", 2)]
    [InlineData("a\nb\n", 2)]
    [InlineData("a\r\nb\r\n", 2)]
    [InlineData("a\rb", 2)]
    [InlineData("a\n\nb", 3)]
    public void CountAndSplitAgree(string text, int expected)
    {
        Assert.Equal(expected, TextLines.Count(text));
        Assert.Equal(expected, TextLines.Split(text).Count);
    }

    [Fact]
    public void SplitKeepsEmptyLinesInside()
    {
        Assert.Equal(new[] { "a", "", "b" }, TextLines.Split("a\r\n\r\nb"));
    }
}

public sealed class IdentifierSplitterTests
{
    [Theory]
    [InlineData("CreateOrderAsync", "Create|Order|Async")]
    [InlineData("HTTPServer", "HTTP|Server")]
    [InlineData("usp_Order_Create", "usp|Order|Create")]
    [InlineData("IOrderRepository", "I|Order|Repository")]
    [InlineData("order2Lines", "order|2|Lines")]
    public void SplitsCompositeIdentifiers(string identifier, string expected)
    {
        Assert.Equal(expected, string.Join("|", IdentifierSplitter.SplitIdentifier(identifier)));
    }

    [Fact]
    public void BuildsSearchTermsOnlyForCompositeIdentifiers()
    {
        var terms = IdentifierSplitter.BuildIdentifierTerms("var id = await service.CreateOrderAsync(request); var x = order;");
        Assert.Contains("create order async", terms);
        Assert.DoesNotContain("service", terms);
    }
}

public sealed class TextDecodingTests
{
    [Fact]
    public void DecodesUtf8WithBom()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("perché")).ToArray();
        var (text, encoding) = TextDecoding.Decode(bytes);
        Assert.Equal("perché", text);
        Assert.Equal("utf-8-bom", encoding);
    }

    [Fact]
    public void FallsBackToWindows1252ForLegacyFiles()
    {
        var bytes = new byte[] { (byte)'p', (byte)'e', (byte)'r', (byte)'c', (byte)'h', 0xE9 };
        var (text, encoding) = TextDecoding.Decode(bytes);
        Assert.Equal("perché", text);
        Assert.Equal("windows-1252", encoding);
    }

    [Fact]
    public void DetectsBinaryButNotUtf16()
    {
        Assert.True(TextDecoding.LooksBinary(new byte[] { 0x4D, 0x5A, 0x00, 0x01 }));
        Assert.False(TextDecoding.LooksBinary(new byte[] { 0xFF, 0xFE, 0x41, 0x00 }));
        Assert.False(TextDecoding.LooksBinary(Encoding.UTF8.GetBytes("plain text")));
    }
}

public sealed class PathUtilitiesTests
{
    [Fact]
    public void DetectsSubPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "lcia-root");
        Assert.True(PathUtilities.IsSameOrSubPath(root, Path.Combine(root, "a", "b.cs")));
        Assert.True(PathUtilities.IsSameOrSubPath(root, root));
        Assert.False(PathUtilities.IsSameOrSubPath(root, root + "-other"));
        Assert.False(PathUtilities.IsSameOrSubPath(root, Path.Combine(root, "..", "x")));
    }

    [Fact]
    public void ResolveInsideRejectsTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "lcia-root");
        Assert.Throws<UnauthorizedAccessException>(() => PathUtilities.ResolveInside(root, "../outside.txt"));
        Assert.EndsWith(Path.Combine("src", "a.cs"), PathUtilities.ResolveInside(root, "src/a.cs"));
    }

    [Theory]
    [InlineData("src/A/b.cs", "src/A", true)]
    [InlineData("src/A", "src/A", true)]
    [InlineData("src/AB/c.cs", "src/A", false)]
    [InlineData("anything.cs", "", true)]
    public void MatchesRelativePrefixesOnFolderBoundaries(string path, string prefix, bool expected)
    {
        Assert.Equal(expected, PathUtilities.HasRelativePrefix(path, prefix));
    }

    [Fact]
    public void SanitizesFileNames()
    {
        Assert.Equal("a_b_c", PathUtilities.SanitizeFileName("a/b:c"));
        Assert.Equal("export", PathUtilities.SanitizeFileName("  ..  "));
    }
}

public sealed class FtsQueryBuilderTests
{
    [Fact]
    public void CombinesWordsWithAnd()
    {
        var query = FtsQueryBuilder.Build("order create");
        Assert.NotNull(query);
        Assert.Equal("\"order\" AND \"create\"", query.MatchExpression);
        Assert.Equal(new[] { "order", "create" }, query.Terms);
    }

    [Fact]
    public void TurnsIdentifiersIntoPhrases()
    {
        var query = FtsQueryBuilder.Build("usp_Order_Create");
        Assert.NotNull(query);
        Assert.Equal("\"usp order create\"", query.MatchExpression);
        Assert.Contains("usp_Order_Create", query.Terms);
    }

    [Fact]
    public void SupportsPrefixPhraseAndExclusion()
    {
        Assert.Equal("\"creat\"*", FtsQueryBuilder.Build("creat*")!.MatchExpression);
        Assert.Equal("(\"exact phrase\") NOT (\"test\")", FtsQueryBuilder.Build("\"exact phrase\" -test")!.MatchExpression);
    }

    [Fact]
    public void NeverPassesRawFtsSyntax()
    {
        var query = FtsQueryBuilder.Build("foo\" OR bar NEAR(x)");
        Assert.NotNull(query);
        Assert.Equal("\"foo\" AND \"or\" AND \"bar\" AND \"near x\"", query.MatchExpression);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-onlyexcluded")]
    [InlineData("*** ---")]
    public void ReturnsNullWithoutSearchableWords(string text)
    {
        Assert.Null(FtsQueryBuilder.Build(text));
    }
}
