namespace Lcia.Application;

/// <summary>
/// Suddivisione in righe coerente in tutta l'applicazione (indicizzazione, ricerca, export).
/// Riconosce CRLF, LF e CR. Un fine riga finale non genera una riga vuota aggiuntiva.
/// </summary>
public static class TextLines
{
    public static List<string> Split(string? text)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return lines;
        }

        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '\r' && c != '\n')
            {
                continue;
            }

            lines.Add(text.Substring(start, i - start));
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }

            start = i + 1;
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return lines;
    }

    public static int Count(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var count = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '\r' && c != '\n')
            {
                continue;
            }

            count++;
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }

            start = i + 1;
        }

        return start < text.Length ? count + 1 : count;
    }

    /// <summary>Riporta il testo con fine riga normalizzati a LF.</summary>
    public static string NormalizeNewLines(string text) => string.Join("\n", Split(text));
}
