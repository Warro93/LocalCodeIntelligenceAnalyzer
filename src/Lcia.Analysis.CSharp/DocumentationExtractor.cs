using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lcia.Analysis.CSharp;

/// <summary>Estrae il testo di &lt;summary&gt; dai commenti di documentazione XML (///).</summary>
internal static partial class DocumentationExtractor
{
    private const int MaxLength = 1000;

    public static string? Extract(SyntaxNode node)
    {
        string? raw = null;
        foreach (var trivia in node.GetLeadingTrivia())
        {
            if (trivia.HasStructure && trivia.GetStructure() is DocumentationCommentTriviaSyntax documentation)
            {
                raw = documentation.ToFullString();
            }
        }

        if (raw is null)
        {
            var builder = new StringBuilder();
            foreach (var trivia in node.GetLeadingTrivia())
            {
                var text = trivia.ToString().TrimStart();
                if (text.StartsWith("///", StringComparison.Ordinal))
                {
                    builder.AppendLine(text);
                }
            }

            if (builder.Length > 0)
            {
                raw = builder.ToString();
            }
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = string.Join("\n", raw.Split('\n').Select(line =>
        {
            var trimmed = line.Trim();
            return trimmed.StartsWith("///", StringComparison.Ordinal) ? trimmed[3..].Trim() : trimmed;
        }));
        cleaned = InlineReference().Replace(cleaned, match => CleanReference(match.Groups["value"].Value));

        string text2;
        try
        {
            var element = XElement.Parse("<doc>" + cleaned + "</doc>", LoadOptions.None);
            text2 = element.Element("summary")?.Value ?? element.Value;
        }
        catch (XmlException)
        {
            text2 = Tags().Replace(cleaned, " ");
        }

        var collapsed = Whitespace().Replace(text2, " ").Trim();
        if (collapsed.Length == 0)
        {
            return null;
        }

        return collapsed.Length <= MaxLength ? collapsed : collapsed[..(MaxLength - 1)] + "…";
    }

    private static string CleanReference(string value)
    {
        var text = value.Length > 2 && value[1] == ':' ? value[2..] : value;
        return text.Replace('{', '<').Replace('}', '>');
    }

    [GeneratedRegex("""<(?:see|seealso|paramref|typeparamref)\s+(?:cref|langword|href|name)\s*=\s*"(?<value>[^"]*)"\s*/>""", RegexOptions.CultureInvariant)]
    private static partial Regex InlineReference();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
