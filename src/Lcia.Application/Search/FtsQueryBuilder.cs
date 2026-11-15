using System.Text;

namespace Lcia.Application;

/// <summary>Query testuale tradotta in espressione FTS5 sicura, con i termini per localizzare le righe.</summary>
public sealed record ParsedTextQuery(string MatchExpression, IReadOnlyList<string> Terms);

/// <summary>
/// Converte l'input dell'utente in un'espressione MATCH di SQLite FTS5 senza permettere
/// l'iniezione di sintassi FTS: ogni termine viene tokenizzato e racchiuso tra virgolette.
/// Sintassi supportata: parole (AND implicito), "frase esatta", prefisso*, -esclusione.
/// </summary>
public static class FtsQueryBuilder
{
    public static ParsedTextQuery? Build(string? userText)
    {
        var text = userText ?? string.Empty;
        var positives = new List<string>();
        var negatives = new List<string>();
        var terms = new List<string>();
        var i = 0;

        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
                continue;
            }

            var negate = false;
            if (text[i] == '-' && i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1]))
            {
                negate = true;
                i++;
            }

            if (text[i] == '"')
            {
                var end = text.IndexOf('"', i + 1);
                var phrase = end < 0 ? text[(i + 1)..] : text[(i + 1)..end];
                i = end < 0 ? text.Length : end + 1;
                var words = Tokenize(phrase);
                if (words.Count == 0)
                {
                    continue;
                }

                var expression = Quote(string.Join(' ', words));
                if (negate)
                {
                    negatives.Add(expression);
                }
                else
                {
                    positives.Add(expression);
                    terms.Add(phrase.Trim());
                }

                continue;
            }

            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            var raw = text[start..i];
            var prefix = raw.EndsWith('*');
            var core = raw.TrimEnd('*');
            var tokens = Tokenize(core);
            if (tokens.Count == 0)
            {
                continue;
            }

            var tokenExpression = Quote(string.Join(' ', tokens)) + (prefix ? "*" : string.Empty);
            if (negate)
            {
                negatives.Add(tokenExpression);
            }
            else
            {
                positives.Add(tokenExpression);
                terms.Add(core.Trim('"', '\'', '(', ')', '[', ']', '{', '}', ',', ';'));
            }
        }

        if (positives.Count == 0)
        {
            return null;
        }

        var match = string.Join(" AND ", positives);
        if (negatives.Count > 0)
        {
            match = "(" + match + ") NOT (" + string.Join(" OR ", negatives) + ")";
        }

        var distinctTerms = terms.Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new ParsedTextQuery(match, distinctTerms);
    }

    /// <summary>Tokenizzazione compatibile con il tokenizer unicode61: sequenze di lettere e cifre, in minuscolo.</summary>
    public static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(char.ToLowerInvariant(c));
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
