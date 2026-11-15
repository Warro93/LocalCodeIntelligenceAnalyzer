using System.Text;

namespace Lcia.Application;

/// <summary>
/// Spezza gli identificatori composti (CamelCase, PascalCase, snake_case, cifre) in parole,
/// così che la ricerca "order" trovi anche <c>CreateOrderAsync</c> e <c>usp_Order_Create</c>.
/// </summary>
public static class IdentifierSplitter
{
    /// <summary>
    /// Restituisce i termini (minuscoli, separati da spazio) ottenuti spezzando gli identificatori
    /// composti presenti nel testo. Gli identificatori semplici sono già coperti dall'indice full-text.
    /// </summary>
    public static string BuildIdentifierTerms(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var i = 0;
        while (i < text.Length)
        {
            if (!IsIdentifierChar(text[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length && IsIdentifierChar(text[i]))
            {
                i++;
            }

            if (i - start < 4)
            {
                continue;
            }

            var token = text.Substring(start, i - start);
            if (!seen.Add(token))
            {
                continue;
            }

            var parts = SplitIdentifier(token);
            if (parts.Count < 2)
            {
                continue;
            }

            foreach (var part in parts)
            {
                builder.Append(part.ToLowerInvariant()).Append(' ');
            }
        }

        return builder.ToString().TrimEnd();
    }

    public static List<string> SplitIdentifier(string identifier)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < identifier.Length; i++)
        {
            var c = identifier[i];
            if (c == '_' || c == '$' || c == '-')
            {
                Flush();
                continue;
            }

            if (current.Length > 0)
            {
                var previous = identifier[i - 1];
                var lowerToUpper = char.IsUpper(c) && char.IsLower(previous);
                var acronymEnd = char.IsUpper(c) && char.IsUpper(previous) && i + 1 < identifier.Length && char.IsLower(identifier[i + 1]);
                var digitBoundary = char.IsDigit(c) != char.IsDigit(previous);
                if (lowerToUpper || acronymEnd || digitBoundary)
                {
                    Flush();
                }
            }

            current.Append(c);
        }

        Flush();
        return parts;

        void Flush()
        {
            if (current.Length > 0)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
        }
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
