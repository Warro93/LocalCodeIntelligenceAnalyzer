using Lcia.Application;
using Lcia.Domain;

namespace Lcia.Tests;

public sealed class SearchIntegrationTests : IClassFixture<DemoShopFixture>
{
    private readonly DemoShopFixture _fixture;

    public SearchIntegrationTests(DemoShopFixture fixture)
    {
        _fixture = fixture;
    }

    private Task<SearchResult> SearchAsync(string text, SearchMode mode, Func<SearchQuery, SearchQuery>? configure = null)
    {
        var query = new SearchQuery { RepositoryId = _fixture.RepositoryId, Text = text, Mode = mode };
        return _fixture.Host.Get<ISearchService>().SearchAsync(configure is null ? query : configure(query));
    }

    [Fact]
    public async Task WordsSearchReturnsPathsAndExactLines()
    {
        var result = await SearchAsync("CreateOrderAsync", SearchMode.Words);
        Assert.Null(result.Warning);
        var expectedLine = SampleRepository.LineOf(_fixture.ReadSource("Services/OrderService.cs"), "public async Task<int> CreateOrderAsync");
        Assert.Contains(result.Hits, h => h.RelativePath == "Services/OrderService.cs" && h.Line == expectedLine);
        Assert.Contains(result.Hits, h => h.RelativePath == "Controllers/OrdersController.cs");
        Assert.All(result.Hits, h => Assert.Equal(SearchHitKind.Content, h.Kind));
    }

    [Fact]
    public async Task WordsSearchMatchesSplitIdentifiers()
    {
        var result = await SearchAsync("order create", SearchMode.Words);
        Assert.Contains(result.Hits, h => h.RelativePath == "Database/StoredProcedures/dbo.usp_Order_Create.sql");
    }

    [Fact]
    public async Task ExactSearchFindsTheStoredProcedureCallAndDefinition()
    {
        var result = await SearchAsync("dbo.usp_Order_Create", SearchMode.ExactText);
        var repositoryLine = SampleRepository.LineOf(_fixture.ReadSource("Repositories/OrderRepository.cs"), "\"dbo.usp_Order_Create\"");
        Assert.Contains(result.Hits, h => h.RelativePath == "Repositories/OrderRepository.cs" && h.Line == repositoryLine);
        Assert.Contains(result.Hits, h => h.RelativePath == "Database/StoredProcedures/dbo.usp_Order_Create.sql" && h.Line == 1);
    }

    [Fact]
    public async Task FiltersAreCombinable()
    {
        var onlyCs = await SearchAsync(@"usp_Order_(Create|Cancel)", SearchMode.Regex, q => q with { Extensions = [".cs"] });
        Assert.NotEmpty(onlyCs.Hits);
        Assert.All(onlyCs.Hits, h => Assert.EndsWith(".cs", h.RelativePath));

        var viewOnly = await SearchAsync("Customers", SearchMode.Words, q => q with { PathContains = "Database/Views" });
        Assert.NotEmpty(viewOnly.Hits);
        Assert.All(viewOnly.Hits, h => Assert.StartsWith("Database/Views/", h.RelativePath));

        var byProject = await SearchAsync("CreateOrderAsync", SearchMode.Words, q => q with { ProjectName = "DemoShop" });
        Assert.NotEmpty(byProject.Hits);
        var unknownProject = await SearchAsync("CreateOrderAsync", SearchMode.Words, q => q with { ProjectName = "Missing" });
        Assert.Empty(unknownProject.Hits);

        var limited = await SearchAsync("order", SearchMode.ExactText, q => q with { MaxResults = 3 });
        Assert.Equal(3, limited.Hits.Count);
        Assert.True(limited.Truncated);
    }

    [Fact]
    public async Task InvalidRegexReturnsWarningInsteadOfThrowing()
    {
        var result = await SearchAsync("(unclosed", SearchMode.Regex);
        Assert.Empty(result.Hits);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public async Task SymbolSearchSupportsWildcardsAndKinds()
    {
        var result = await SearchAsync("*Repository", SearchMode.Symbol, q => q with { SymbolKinds = [SymbolKind.Interface] });
        Assert.Contains(result.Hits, h => h.Preview.Contains("IOrderRepository", StringComparison.Ordinal));
        Assert.Contains(result.Hits, h => h.Preview.Contains("ICustomerRepository", StringComparison.Ordinal));
        Assert.All(result.Hits, h => Assert.Equal(SearchHitKind.Symbol, h.Kind));
        Assert.All(result.Hits, h => Assert.StartsWith("Interfaccia", h.Detail));
    }

    [Fact]
    public async Task ReferenceSearchFindsDependenciesAndRegistrations()
    {
        var result = await SearchAsync("IOrderRepository", SearchMode.References);
        Assert.Contains(result.Hits, h => h.RelativePath == "Services/OrderService.cs" && h.Detail!.Contains("costruttore", StringComparison.Ordinal));
        Assert.Contains(result.Hits, h => h.RelativePath == "Program.cs" && h.Detail!.Contains("Registrazione DI", StringComparison.Ordinal));

        var onlyDi = await SearchAsync("IOrderRepository", SearchMode.References, q => q with { RelationKinds = [RelationKind.DiRegistration] });
        Assert.All(onlyDi.Hits, h => Assert.Equal("Program.cs", h.RelativePath));
    }
}
