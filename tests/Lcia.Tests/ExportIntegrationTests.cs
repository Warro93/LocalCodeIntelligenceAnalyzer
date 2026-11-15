using Lcia.Application;

namespace Lcia.Tests;

public sealed class ExportIntegrationTests : IClassFixture<DemoShopFixture>
{
    private const string FakePassword = "Demo-Only-Not-A-Real-Password-123";
    private readonly DemoShopFixture _fixture;

    public ExportIntegrationTests(DemoShopFixture fixture)
    {
        _fixture = fixture;
    }

    private IExportService Export => _fixture.Host.Get<IExportService>();

    private ExportRequest Request(TempDirectory output) => new()
    {
        RepositoryId = _fixture.RepositoryId,
        OutputDirectory = output.Path,
    };

    [Fact]
    public async Task FullTextExportContainsDelimitersMetadataAndFullSource()
    {
        using var output = new TempDirectory("lcia-export");
        var result = await Export.ExportAsync(Request(output) with { IncludeLineNumbers = false, BaseFileName = "demo" });

        var part = Assert.Single(result.Parts);
        Assert.Null(result.IndexFilePath);
        Assert.Equal(Path.Combine(output.Path, "demo.txt"), part.FilePath);
        var text = File.ReadAllText(part.FilePath);

        Assert.Contains("LOCAL CODE INTELLIGENCE ANALYZER - FULL PROJECT CONTEXT EXPORT", text);
        Assert.Contains("==================================================\nFILE: Services/OrderService.cs\n==================================================", text.ReplaceLineEndings("\n"));
        Assert.Contains("NAMESPACE: DemoShop.Services", text);
        Assert.Contains("CLASS: OrderService", text);
        Assert.Contains("- IOrderRepository", text);
        Assert.Contains("- ILogger<OrderService>", text);
        Assert.Contains("END OF FILE: Services/OrderService.cs", text);
        Assert.Contains("SOLUTIONS AND PROJECTS", text);
        Assert.Contains("PROJECT: DemoShop.csproj", text);
        Assert.Contains("SDK: Microsoft.NET.Sdk.Web", text);
        Assert.Contains("DI REGISTRATIONS:", text);

        var source = TextLines.NormalizeNewLines(_fixture.ReadSource("Services/OrderService.cs"));
        Assert.Contains(source, text.ReplaceLineEndings("\n"));
        Assert.Equal(result.FilesExported, result.Parts.Sum(p => p.Files.Count));
    }

    [Fact]
    public async Task SecretsAreRedactedByDefault()
    {
        using var output = new TempDirectory("lcia-export");
        var result = await Export.ExportAsync(Request(output));
        var text = File.ReadAllText(result.Parts[0].FilePath);

        Assert.DoesNotContain(FakePassword, text);
        Assert.Contains("[REDACTED:connection-string-password]", text);
        Assert.Contains(result.Redactions, r => r.RelativePath == "appsettings.json" && r.Line == 3);
        Assert.Contains("REDACTED VALUES", text);
    }

    [Fact]
    public async Task UnredactedExportRequiresExplicitConfirmation()
    {
        using var output = new TempDirectory("lcia-export");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Export.ExportAsync(Request(output) with { RedactSecrets = false }));
        Assert.Empty(Directory.GetFiles(output.Path));

        var confirmed = await Export.ExportAsync(Request(output) with { RedactSecrets = false, UnredactedExportConfirmed = true });
        Assert.Contains(FakePassword, File.ReadAllText(confirmed.Parts[0].FilePath));
    }

    [Fact]
    public async Task LineNumbersAreAddedByDefault()
    {
        using var output = new TempDirectory("lcia-export");
        var result = await Export.ExportAsync(Request(output) with { IncludeExtensions = [".cs"] });
        var text = File.ReadAllText(result.Parts[0].FilePath);
        Assert.Contains("  1 | using DemoShop.Models;", text);
    }

    [Fact]
    public async Task MarkdownExportUsesFencedCodeBlocks()
    {
        using var output = new TempDirectory("lcia-export");
        var result = await Export.ExportAsync(Request(output) with { Format = ExportFormat.Markdown });
        Assert.EndsWith(".md", result.Parts[0].FilePath);
        var text = File.ReadAllText(result.Parts[0].FilePath);
        Assert.Contains("## FILE: `Services/OrderService.cs`", text);
        Assert.Contains("```csharp", text);
        Assert.Contains("```sql", text);
    }

    [Fact]
    public async Task LargeExportsAreSplitWithAnIndexFile()
    {
        using var output = new TempDirectory("lcia-export");
        var result = await Export.ExportAsync(Request(output) with { MaxPartTokens = 2000, BaseFileName = "split" });

        Assert.True(result.Parts.Count > 1);
        Assert.NotNull(result.IndexFilePath);
        Assert.True(File.Exists(result.IndexFilePath));
        Assert.All(result.Parts, p => Assert.True(File.Exists(p.FilePath)));
        Assert.EndsWith("split.part002.txt", result.Parts[1].FilePath);

        var exported = result.Parts.SelectMany(p => p.Files).Select(f => f.Split(" (segment", StringSplitOptions.None)[0]).Distinct().ToList();
        Assert.Equal(result.FilesExported, exported.Count);

        var index = File.ReadAllText(result.IndexFilePath);
        Assert.Contains("PART 002", index);
        Assert.Contains("Services/OrderService.cs", index);
        Assert.Contains("PART 002", File.ReadAllText(result.Parts[1].FilePath));
    }

    [Fact]
    public async Task CustomScopeAndFiltersSelectTheRequestedFiles()
    {
        using var output = new TempDirectory("lcia-export");
        var custom = await Export.ExportAsync(Request(output) with
        {
            Scope = ExportScope.Custom,
            SelectedFiles = ["Services/OrderService.cs", "Repositories/OrderRepository.cs"],
        });
        Assert.Equal(2, custom.FilesExported);

        var plan = await Export.PlanAsync(Request(output) with { IncludeExtensions = [".sql"], ExcludePathPrefixes = ["Database/Types"] });
        Assert.NotEmpty(plan.Files);
        Assert.All(plan.Files, f => Assert.EndsWith(".sql", f.RelativePath));
        Assert.DoesNotContain(plan.Files, f => f.RelativePath.StartsWith("Database/Types/", StringComparison.Ordinal));
        Assert.True(plan.EstimatedTokens > 0);

        var folders = await Export.PlanAsync(Request(output) with { IncludePathPrefixes = ["Services"] });
        Assert.Equal(2, folders.Files.Count);
    }

    [Fact]
    public async Task RefusesToWriteInsideTheRepositoryAndNeverModifiesIt()
    {
        var before = SampleRepository.Snapshot(_fixture.Root);
        var request = new ExportRequest { RepositoryId = _fixture.RepositoryId, OutputDirectory = Path.Combine(_fixture.Root, "exports") };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Export.ExportAsync(request));
        Assert.False(Directory.Exists(Path.Combine(_fixture.Root, "exports")));

        using var output = new TempDirectory("lcia-export");
        await Export.ExportAsync(Request(output));
        var after = SampleRepository.Snapshot(_fixture.Root);
        Assert.Equal(before, after);
    }
}

/// <summary>Esportazione dopo una modifica non ancora indicizzata (repository dedicato).</summary>
public sealed class ExportModifiedFileTests
{
    [Fact]
    public async Task FlagsFilesModifiedAfterIndexing()
    {
        using var host = new TestHost();
        using var work = new TempDirectory();
        using var output = new TempDirectory("lcia-export");
        var root = SampleRepository.CopyDemoShop(work);
        var indexed = await host.Get<IndexingService>().IndexAsync(new IndexingRequest(root, new ScanOptions()));
        File.AppendAllText(Path.Combine(root, "Program.cs"), "\n// cambiato dopo l'indicizzazione\n");

        var result = await host.Get<IExportService>().ExportAsync(new ExportRequest
        {
            RepositoryId = indexed.Repository.Id,
            OutputDirectory = output.Path,
        });

        Assert.Contains("Program.cs", result.ModifiedSinceIndex);
        Assert.Contains("cambiato dopo l'indicizzazione", File.ReadAllText(result.Parts[0].FilePath));
    }
}
