using System.Text.RegularExpressions;
using Lcia.Domain;

namespace Lcia.Application;

/// <summary>Valore sensibile individuato: solo riga e categoria, mai il valore.</summary>
public sealed record SecretFinding(int Line, string Category);

/// <summary>Testo con i valori sensibili sostituiti da [REDACTED:categoria]. Il numero di righe non cambia.</summary>
public sealed record RedactionResult(string Text, IReadOnlyList<SecretFinding> Findings);

/// <summary>Rilevamento e oscuramento di password, token, chiavi private, certificati e connection string.</summary>
public interface ISecretScanner
{
    /// <summary>Oscura i segreti nel testo; i fine riga vengono normalizzati a LF e il numero di righe resta invariato.</summary>
    RedactionResult Redact(string text, SourceLanguage language);

    /// <summary>Oscura i segreti in una singola riga (usato anche per i log).</summary>
    string RedactLine(string line);
}

/// <summary>Nomi di file che contengono tipicamente segreti: esclusi dalle esportazioni salvo conferma esplicita.</summary>
public static class SensitiveFiles
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "secrets.json", ".env", "id_rsa", "id_dsa", "id_ecdsa", "id_ed25519", "credentials.json", "credentials.xml",
    };

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pfx", ".p12", ".pem", ".key", ".snk", ".publishsettings", ".keystore", ".jks", ".kdbx",
    };

    public static bool IsSensitive(string relativePath)
    {
        var name = Path.GetFileName(relativePath);
        if (Names.Contains(name) || name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Extensions.Contains(Path.GetExtension(name));
    }
}

/// <summary>
/// Implementazione a regole (espressioni regolari con timeout). Approccio conservativo:
/// preferisce oscurare un valore innocuo piuttosto che esportare un segreto.
/// </summary>
public sealed class RegexSecretScanner : ISecretScanner
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
    private const string SecretKeyWords = "password|passwd|pwd|secret|apikey|api_key|api-key|accesskey|access_key|privatekey|private_key|clientsecret|client_secret|token|credential";

    [Flags]
    private enum Scope
    {
        Code = 1,
        Config = 2,
        Sql = 4,
        Text = 8,
        All = Code | Config | Sql | Text,
        NonCode = Config | Sql | Text,
    }

    private sealed record Rule(string Category, Regex Pattern, Scope Scope);

    private static Regex R(string pattern, RegexOptions extra = RegexOptions.None) => new(pattern, Options | extra, MatchTimeout);

    private static readonly Rule[] Rules =
    [
        // Blocchi PEM su una sola riga (es. chiavi con \n letterali dentro JSON).
        new("private-key", R(@"-----BEGIN [A-Z0-9 ]*(?:PRIVATE KEY|CERTIFICATE)(?: BLOCK)?-----(?<value>.+?)(?=-----END)"), Scope.All),

        // Token con formato noto.
        new("github-token", R(@"\b(?:gh[pousr]_[A-Za-z0-9]{36,255}|github_pat_[A-Za-z0-9_]{22,255})\b"), Scope.All),
        new("aws-access-key", R(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b"), Scope.All),
        new("aws-secret-key", R(@"(?i)aws_?secret_?access_?key\W{0,6}(?<value>[A-Za-z0-9/+=]{40})"), Scope.All),
        new("slack-token", R(@"\bxox[abposr]-[A-Za-z0-9-]{10,}"), Scope.All),
        new("google-api-key", R(@"\bAIza[0-9A-Za-z_\-]{35}"), Scope.All),
        new("stripe-key", R(@"\b[rs]k_(?:live|test)_[0-9A-Za-z]{16,}"), Scope.All),
        new("api-key", R(@"\bsk-(?:ant-|proj-)?[A-Za-z0-9_\-]{20,}"), Scope.All),
        new("jwt", R(@"\beyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}"), Scope.All),
        new("azure-account-key", R(@"(?i)\bAccountKey\s*=\s*(?<value>[A-Za-z0-9+/=]{20,})"), Scope.All),
        new("shared-access-key", R(@"(?i)\b(?:SharedAccessKey|SharedAccessSignature|sig)\s*=\s*(?<value>[A-Za-z0-9%+/=]{16,})"), Scope.All),
        new("bearer-token", R(@"(?i)\bBearer\s+(?<value>[A-Za-z0-9\-._~+/]{16,}=*)"), Scope.All),
        new("url-credentials", R(@"(?i)\b[a-z][a-z0-9+.\-]*://[^\s/:@'""]+:(?<value>[^\s@/'""]{3,})@"), Scope.All),

        // Connection string e assegnazioni nei file di configurazione, SQL e testo.
        new("connection-string-password", R(@"(?i)(?<![A-Za-z])(?:password|pwd)\s*=\s*(?<value>""[^""]*""|'[^']*'|[^;""'<>\r\n]+)"), Scope.NonCode),
        new("sql-password", R(@"(?i)\bIDENTIFIED\s+BY\s+(?<value>'[^']*'|""[^""]*""|\S+)"), Scope.Sql),
        new("config-secret", R(@"(?i)[""']?[\w.\-]*(?:" + SecretKeyWords + @")[\w.\-]*[""']?\s*:\s*(?<q>[""'])(?<value>[^""'\r\n]+)\k<q>"), Scope.Config | Scope.Text),
        new("config-secret", R(@"(?i)^\s*[\w.\-]*(?:" + SecretKeyWords + @")[\w.\-]*\s*[:=]\s*(?<value>[^\s""'#][^#\r\n]*?)\s*$"), Scope.Config | Scope.Text),
        new("config-secret", R(@"(?i)\bkey\s*=\s*""[^""]*(?:" + SecretKeyWords + @"|connectionstring)[^""]*""\s+value\s*=\s*""(?<value>[^""]*)"""), Scope.Config | Scope.Text),
        new("config-secret", R(@"(?i)<(?<tag>[\w:.\-]*(?:" + SecretKeyWords + @")[\w:.\-]*)(?:\s[^>]*)?>(?<value>[^<]+)</\k<tag>\s*>"), Scope.Config | Scope.Text),

        // Codice: assegnazione di una stringa letterale a un nome "sensibile".
        new("hardcoded-secret", R(@"(?i)\b[\w]*(?:" + SecretKeyWords + @")[\w]*[""']?\s*(?:=|:|=>)\s*@?\$?(?<q>[""'])(?<value>[^""'\r\n]{4,})\k<q>"), Scope.Code),
    ];

    private static readonly Regex StringLiteral = R(@"""(?<content>(?:[^""\\\r\n]|\\.)*)""");
    private static readonly Regex InsideLiteralPassword = R(@"(?i)(?<![A-Za-z])(?:password|pwd)\s*=\s*(?<value>[^;""'\r\n]+)");
    private static readonly Regex PemBegin = R(@"-----BEGIN (?<kind>[A-Z0-9 ]*?)(?:PRIVATE KEY|CERTIFICATE)(?: BLOCK)?-----");
    private static readonly Regex PemEnd = R(@"-----END [A-Z0-9 ]*-----");
    private static readonly Regex Placeholder = R(@"^(?:\$\{[^}]*\}|\$\([^)]*\)|%[^%]+%|\{\{?[^}]*\}?\}|<[^>]*>|\*+|x+|\?+|\d{1,6}|true|false|null|none|\[REDACTED:[^\]]*\])$", RegexOptions.IgnoreCase);

    public RedactionResult Redact(string text, SourceLanguage language)
    {
        var lines = TextLines.Split(text);
        var findings = new List<SecretFinding>();
        var scope = ScopeFor(language);
        var insidePem = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i];

            if (insidePem)
            {
                if (SafeIsMatch(PemEnd, line))
                {
                    insidePem = false;
                }
                else if (line.Trim().Length > 0)
                {
                    lines[i] = Mask("private-key");
                }

                continue;
            }

            if (SafeIsMatch(PemBegin, line) && !SafeIsMatch(PemEnd, line))
            {
                findings.Add(new SecretFinding(lineNumber, line.Contains("CERTIFICATE", StringComparison.Ordinal) ? "certificate" : "private-key"));
                insidePem = true;
                continue;
            }

            lines[i] = RedactSingleLine(line, scope, lineNumber, findings);
        }

        return new RedactionResult(string.Join("\n", lines), findings);
    }

    public string RedactLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return line;
        }

        var findings = new List<SecretFinding>();
        return RedactSingleLine(line, Scope.Text, 0, findings);
    }

    private static string RedactSingleLine(string line, Scope scope, int lineNumber, List<SecretFinding> findings)
    {
        if (line.Length == 0)
        {
            return line;
        }

        var result = line;
        foreach (var rule in Rules)
        {
            if ((rule.Scope & scope) != 0)
            {
                result = Apply(rule.Pattern, rule.Category, result, lineNumber, findings);
            }
        }

        if (scope == Scope.Code)
        {
            result = RedactInsideStringLiterals(result, lineNumber, findings);
        }

        return result;
    }

    private static string RedactInsideStringLiterals(string line, int lineNumber, List<SecretFinding> findings)
    {
        try
        {
            return StringLiteral.Replace(line, match =>
            {
                var content = match.Groups["content"].Value;
                if (content.Length == 0)
                {
                    return match.Value;
                }

                var replaced = Apply(InsideLiteralPassword, "connection-string-password", content, lineNumber, findings);
                return ReferenceEquals(replaced, content) || replaced == content ? match.Value : "\"" + replaced + "\"";
            });
        }
        catch (RegexMatchTimeoutException)
        {
            findings.Add(new SecretFinding(lineNumber, "scan-timeout"));
            return Mask("scan-timeout");
        }
    }

    private static string Apply(Regex pattern, string category, string input, int lineNumber, List<SecretFinding> findings)
    {
        try
        {
            return pattern.Replace(input, match =>
            {
                var value = match.Groups["value"];
                if (!value.Success)
                {
                    findings.Add(new SecretFinding(lineNumber, category));
                    return Mask(category);
                }

                var raw = value.Value.Trim().Trim('"', '\'');
                if (raw.Length == 0 || SafeIsMatch(Placeholder, raw))
                {
                    return match.Value;
                }

                findings.Add(new SecretFinding(lineNumber, category));
                var relative = value.Index - match.Index;
                return match.Value[..relative] + Mask(category) + match.Value[(relative + value.Length)..];
            });
        }
        catch (RegexMatchTimeoutException)
        {
            // Non è stato possibile verificare la riga: per sicurezza viene oscurata per intero.
            findings.Add(new SecretFinding(lineNumber, "scan-timeout"));
            return Mask("scan-timeout");
        }
    }

    private static bool SafeIsMatch(Regex regex, string input)
    {
        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static string Mask(string category) => $"[REDACTED:{category}]";

    private static Scope ScopeFor(SourceLanguage language) => language switch
    {
        SourceLanguage.CSharp or SourceLanguage.VisualBasic or SourceLanguage.FSharp or SourceLanguage.JavaScript
            or SourceLanguage.TypeScript or SourceLanguage.Python or SourceLanguage.Java or SourceLanguage.Go
            or SourceLanguage.Php or SourceLanguage.Razor => Scope.Code,
        SourceLanguage.TSql => Scope.Sql,
        SourceLanguage.Json or SourceLanguage.Xml or SourceLanguage.Config or SourceLanguage.Yaml
            or SourceLanguage.MsBuild or SourceLanguage.Solution or SourceLanguage.Xaml => Scope.Config,
        _ => Scope.Text,
    };
}
