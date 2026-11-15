using Lcia.Application;
using Lcia.Domain;

namespace Lcia.Tests;

/// <summary>
/// I valori "segreti" usati nei test sono costruiti a runtime, così il sorgente dei test
/// non contiene stringhe che sembrano credenziali reali.
/// </summary>
public sealed class SecretScannerTests
{
    private readonly RegexSecretScanner _scanner = new();

    [Fact]
    public void RedactsJsonPasswordValue()
    {
        var secret = "Super" + "Secret" + "123";
        var result = _scanner.Redact($"{{\n  \"Password\": \"{secret}\"\n}}", SourceLanguage.Json);
        Assert.DoesNotContain(secret, result.Text);
        Assert.Contains("[REDACTED:", result.Text);
        Assert.Contains(result.Findings, f => f.Line == 2);
    }

    [Fact]
    public void RedactsOnlyThePasswordInsideConnectionStrings()
    {
        var secret = "P@ss" + "w0rd!";
        var json = $"{{ \"Shop\": \"Server=.;Database=X;User Id=u;Password={secret};Encrypt=True\" }}";
        var result = _scanner.Redact(json, SourceLanguage.Json);
        Assert.DoesNotContain(secret, result.Text);
        Assert.Contains("Server=.;Database=X;User Id=u;Password=[REDACTED:connection-string-password];Encrypt=True", result.Text);
    }

    [Fact]
    public void RedactsHardcodedSecretsInCodeButNotCodeReferences()
    {
        var apiKey = "abcd1234" + "efgh5678";
        var password = "Secr" + "3t99";
        var code = string.Join("\n",
            $"private const string ApiKey = \"{apiKey}\";",
            "var password = request.Password;",
            $"var cs = \"Server=.;Password={password};\";",
            "if (password == null) return;");
        var result = _scanner.Redact(code, SourceLanguage.CSharp);
        var lines = TextLines.Split(result.Text);
        Assert.DoesNotContain(apiKey, result.Text);
        Assert.DoesNotContain(password, result.Text);
        Assert.Equal("var password = request.Password;", lines[1]);
        Assert.Equal("if (password == null) return;", lines[3]);
        Assert.Equal(4, lines.Count);
    }

    [Fact]
    public void RedactsKnownTokenFormatsInAnyFile()
    {
        var github = "ghp_" + new string('a', 36);
        var aws = "AKIA" + "ABCDEFGHIJKLMNOP";
        var text = $"token1 {github}\naws {aws}\nplain text";
        var result = _scanner.Redact(text, SourceLanguage.Markdown);
        Assert.DoesNotContain(github, result.Text);
        Assert.DoesNotContain(aws, result.Text);
        Assert.Contains(result.Findings, f => f.Category == "github-token" && f.Line == 1);
        Assert.Contains(result.Findings, f => f.Category == "aws-access-key" && f.Line == 2);
        Assert.EndsWith("plain text", result.Text);
    }

    [Fact]
    public void MasksPrivateKeyBlocksPreservingLineCount()
    {
        var begin = "-----BEGIN " + "RSA PRIVATE KEY-----";
        var end = "-----END " + "RSA PRIVATE KEY-----";
        var body = "MIIEow" + "IBAAKCAQEA1234567890";
        var text = string.Join("\n", "before", begin, body, body, end, "after");
        var result = _scanner.Redact(text, SourceLanguage.Unknown);
        var lines = TextLines.Split(result.Text);
        Assert.Equal(6, lines.Count);
        Assert.Equal(begin, lines[1]);
        Assert.Equal("[REDACTED:private-key]", lines[2]);
        Assert.Equal(end, lines[4]);
        Assert.Equal("after", lines[5]);
        Assert.DoesNotContain(body, result.Text);
        Assert.Single(result.Findings);
    }

    [Fact]
    public void KeepsPlaceholders()
    {
        var text = "{ \"Password\": \"${DB_PASSWORD}\", \"TokenLifetimeMinutes\": \"30\" }";
        var result = _scanner.Redact(text, SourceLanguage.Json);
        Assert.Equal(text, result.Text);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void RedactsXmlConfigurationValues()
    {
        var secret = "xyz" + "987654";
        var xml = $"<appSettings>\n  <add key=\"PaymentApiKey\" value=\"{secret}\" />\n</appSettings>";
        var result = _scanner.Redact(xml, SourceLanguage.Config);
        Assert.DoesNotContain(secret, result.Text);
        Assert.Contains("key=\"PaymentApiKey\"", result.Text);
    }

    [Fact]
    public void RedactsLogLines()
    {
        var secret = "abc" + "123";
        Assert.DoesNotContain(secret, _scanner.RedactLine($"Connessione fallita: Server=db;Password={secret};"));
        Assert.Equal("Messaggio normale", _scanner.RedactLine("Messaggio normale"));
    }

    [Theory]
    [InlineData("config/secrets.json", true)]
    [InlineData(".env", true)]
    [InlineData("deploy/.env.production", true)]
    [InlineData("certs/server.pfx", true)]
    [InlineData("src/Program.cs", false)]
    [InlineData("appsettings.json", false)]
    public void RecognizesSensitiveFileNames(string path, bool expected)
    {
        Assert.Equal(expected, SensitiveFiles.IsSensitive(path));
    }
}
