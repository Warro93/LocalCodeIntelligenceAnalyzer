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
    public void RedactsWholeConnectionStringsByDefault()
    {
        var secret = "P@ss" + "w0rd!";
        var json = $"{{ \"Shop\": \"Server=db01.contoso.local;Database=Shop;User Id=u;Password={secret};Encrypt=True\" }}";
        var result = _scanner.Redact(json, SourceLanguage.Json);
        Assert.Equal("{ \"Shop\": \"[REDACTED:connection-string]\" }", result.Text);
        Assert.DoesNotContain("contoso", result.Text);
        Assert.Contains(result.Findings, f => f.Category == "connection-string" && f.Line == 1);
    }

    [Fact]
    public void CanRedactOnlyThePasswordInsideConnectionStrings()
    {
        var secret = "P@ss" + "w0rd!";
        var json = $"{{ \"Shop\": \"Server=.;Database=X;User Id=u;Password={secret};Encrypt=True\" }}";
        var result = _scanner.Redact(json, SourceLanguage.Json, new RedactionOptions { FullConnectionStrings = false });
        Assert.DoesNotContain(secret, result.Text);
        Assert.Contains("Server=.;Database=X;User Id=u;Password=[REDACTED:connection-string-password];Encrypt=True", result.Text);
    }

    [Fact]
    public void RedactsConnectionStringsInXmlCodeSqlAndLogs()
    {
        var xml = "<add name=\"Shop\" connectionString=\"Data Source=.\\SQLEXPRESS;Initial Catalog=Shop;Integrated Security=True\" />";
        Assert.Equal("<add name=\"Shop\" connectionString=\"[REDACTED:connection-string]\" />", _scanner.Redact(xml, SourceLanguage.Config).Text);

        var code = "var cs = \"Host=pg01;Port=5432;Username=app;Password=" + "pw12" + "34;Database=shop\";";
        Assert.Equal("var cs = \"[REDACTED:connection-string]\";", _scanner.Redact(code, SourceLanguage.CSharp).Text);

        var azure = "DefaultEndpointsProtocol=https;AccountName=acct;AccountKey=" + new string('A', 40) + "==;EndpointSuffix=core.windows.net";
        Assert.Equal("[REDACTED:connection-string]", _scanner.Redact(azure, SourceLanguage.Unknown).Text);

        Assert.Equal("Errore su [REDACTED:connection-string]", _scanner.RedactLine("Errore su Server=db;Database=x;"));
    }

    [Fact]
    public void LeavesCodeAssignmentsAndPlaceholderConnectionStringsAlone()
    {
        Assert.Equal("options.Host = host; options.Port = port;", _scanner.Redact("options.Host = host; options.Port = port;", SourceLanguage.CSharp).Text);
        var placeholders = "{ \"Shop\": \"Server=${DB_HOST};Database=${DB_NAME}\" }";
        Assert.Equal(placeholders, _scanner.Redact(placeholders, SourceLanguage.Json).Text);
        var flagsOnly = "{ \"Options\": \"Encrypt=True;TrustServerCertificate=True\" }";
        Assert.Equal(flagsOnly, _scanner.Redact(flagsOnly, SourceLanguage.Json).Text);
        var assembly = "Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";
        Assert.Equal(assembly, _scanner.Redact(assembly, SourceLanguage.Config).Text);
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
    public void RedactsNewTokenFormatsAndSqlPasswords()
    {
        var npm = "npm_" + new string('b', 36);
        var sendgrid = "SG." + new string('c', 22) + "." + new string('d', 43);
        var basic = "Basic " + "dXNlcjpwYXNz" + "d29yZDEyMzQ=";
        var text = $"npm {npm}\nsg {sendgrid}\nAuthorization: {basic}";
        var result = _scanner.Redact(text, SourceLanguage.Unknown);
        Assert.DoesNotContain(npm, result.Text);
        Assert.DoesNotContain(sendgrid, result.Text);
        Assert.DoesNotContain("d29yZDEyMzQ=", result.Text);

        var login = "CREATE LOGIN app WITH PASSWORD = N'" + "Sql" + "Pass1" + "';";
        var sql = _scanner.Redact(login, SourceLanguage.TSql).Text;
        Assert.DoesNotContain("SqlPass1", sql);
        Assert.StartsWith("CREATE LOGIN app WITH PASSWORD = ", sql);
    }

    [Fact]
    public void RedactsSecretAttributesInXml()
    {
        var secret = "Xml" + "Secret" + "42";
        var xml = $"<smtp user=\"mailer\" password=\"{secret}\" />";
        var result = _scanner.Redact(xml, SourceLanguage.Config);
        Assert.DoesNotContain(secret, result.Text);
        Assert.Contains("user=\"mailer\"", result.Text);
        Assert.Equal("<PasswordBox Password=\"{Binding Pwd}\" />", _scanner.Redact("<PasswordBox Password=\"{Binding Pwd}\" />", SourceLanguage.Xaml).Text);
    }

    [Fact]
    public void PersonalDataIsMaskedOnlyWhenRequested()
    {
        var text = "Referente: mario.rossi@azienda.example, server 192.168.10.20, locale 127.0.0.1";
        Assert.Equal(text, _scanner.Redact(text, SourceLanguage.Unknown).Text);

        var options = new RedactionOptions { PersonalData = true };
        var result = _scanner.Redact(text, SourceLanguage.Unknown, options);
        Assert.Equal("Referente: [REDACTED:email], server [REDACTED:ip-address], locale 127.0.0.1", result.Text);
        Assert.Contains(result.Findings, f => f.Category == "email");
        Assert.Contains(result.Findings, f => f.Category == "ip-address");

        var version = "<AssemblyVersion>1.2.3.4</AssemblyVersion>";
        Assert.Equal(version, _scanner.Redact(version, SourceLanguage.MsBuild, options).Text);
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
