using Lcia.Application;
using Lcia.Domain;

namespace Lcia.Tests;

/// <summary>Indicizzazione reale del progetto DemoShop (scanner + Roslyn + SQLite/FTS5).</summary>
public sealed class IndexingIntegrationTests
{
    [Fact]
    public async Task IndexesDemoShopWithSymbolsProjectsAndIncrementalUpdates()
    {
        using var host = new TestHost();
        using var work = new TempDirectory();
        var root = SampleRepository.CopyDemoShop(work);
        var indexing = host.Get<IndexingService>();
        var store = host.Get<IIndexStore>();

        var first = await indexing.IndexAsync(new IndexingRequest(root, new ScanOptions()));
        Assert.Equal(ScanRunStatus.Completed, first.Run.Status);
        Assert.True(first.Run.FilesIndexed >= 20, $"File indicizzati: {first.Run.FilesIndexed}");
        Assert.Equal(first.Run.FilesSeen, first.Run.FilesIndexed);

        var files = await store.ListFilesAsync(first.Repository.Id);
        var orderService = Assert.Single(files, f => f.RelativePath == "Services/OrderService.cs");
        Assert.Equal(SourceLanguage.CSharp, orderService.Language);
        Assert.Equal(SupportLevel.Syntactic, orderService.AnalysisLevel);
        Assert.Contains(files, f => f.RelativePath == "Database/StoredProcedures/dbo.usp_Order_Create.sql" && f.Language == SourceLanguage.TSql);

        var symbols = await store.ListSymbolsAsync(orderService.Id);
        var method = Assert.Single(symbols, s => s.Name == "CreateOrderAsync");
        Assert.Equal(SampleRepository.LineOfFile(Path.Combine(root, "Services", "OrderService.cs"), "public async Task<int> CreateOrderAsync"), method.StartLine);
        var type = Assert.Single(symbols, s => s.Kind == SymbolKind.Class);
        Assert.Equal(type.Id, method.ParentId);

        var relations = await store.ListRelationsAsync(orderService.Id);
        Assert.Contains(relations, r => r.Kind == RelationKind.ConstructorInjection && r.TargetName == "IOrderRepository" && r.SourceSymbolId == type.Id);

        var projects = await store.ListProjectsAsync(first.Repository.Id);
        var demo = Assert.Single(projects, p => p.Name == "DemoShop");
        Assert.Equal("Microsoft.NET.Sdk.Web", demo.Sdk);
        Assert.Equal(demo.Id, orderService.ProjectId);

        var stats = await store.GetStatisticsAsync(first.Repository.Id);
        Assert.Equal(first.Run.FilesIndexed, stats.IndexedFiles);
        Assert.True(stats.Symbols > 40, $"Simboli: {stats.Symbols}");
        Assert.Equal(1, stats.Projects);

        // Seconda esecuzione: nessun file da rielaborare.
        var second = await indexing.IndexAsync(new IndexingRequest(root, new ScanOptions()));
        Assert.Equal(ScanRunStatus.Completed, second.Run.Status);
        Assert.Equal(0, second.Run.FilesIndexed);
        Assert.Equal(first.Run.FilesSeen, second.Run.FilesUnchanged);

        // Modifica di un file e cancellazione di un altro.
        File.AppendAllText(Path.Combine(root, "Services", "OrderService.cs"), "\n// modifica di test\n");
        File.Delete(Path.Combine(root, "Models", "Orders.cs"));
        var third = await indexing.IndexAsync(new IndexingRequest(root, new ScanOptions()));
        Assert.Equal(1, third.Run.FilesIndexed);
        Assert.Equal(1, third.Run.FilesRemoved);
        Assert.DoesNotContain(await store.ListFilesAsync(first.Repository.Id), f => f.RelativePath == "Models/Orders.cs");

        // Reindicizzazione forzata: tutti i file vengono rielaborati.
        var forced = await indexing.IndexAsync(new IndexingRequest(root, new ScanOptions { ForceReindex = true }));
        Assert.Equal(forced.Run.FilesSeen, forced.Run.FilesIndexed);
    }

    [Fact]
    public async Task NeverModifiesTheAnalyzedRepository()
    {
        using var host = new TestHost();
        using var work = new TempDirectory();
        var root = SampleRepository.CopyDemoShop(work);
        var before = SampleRepository.Snapshot(root);

        await host.Get<IndexingService>().IndexAsync(new IndexingRequest(root, new ScanOptions()));

        var after = SampleRepository.Snapshot(root);
        Assert.Equal(before.Count, after.Count);
        foreach (var (path, hash) in before)
        {
            Assert.Equal(hash, after[path]);
        }
    }

    [Fact]
    public async Task CancellationIsRecordedAndTheNextRunCompletes()
    {
        using var host = new TestHost();
        using var work = new TempDirectory();
        var root = SampleRepository.CopyDemoShop(work);
        var indexing = host.Get<IndexingService>();
        var store = host.Get<IIndexStore>();

        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress<IndexingProgress>(p =>
        {
            if (p.Phase == IndexingPhase.Comparing)
            {
                cancellation.Cancel();
            }
        });

        var cancelled = await indexing.IndexAsync(new IndexingRequest(root, new ScanOptions()), progress, cancellation.Token);
        Assert.Equal(ScanRunStatus.Cancelled, cancelled.Run.Status);
        var runs = await store.ListScanRunsAsync(cancelled.Repository.Id, 5);
        Assert.Equal(ScanRunStatus.Cancelled, runs[0].Status);

        var resumed = await indexing.IndexAsync(new IndexingRequest(root, new ScanOptions()));
        Assert.Equal(ScanRunStatus.Completed, resumed.Run.Status);
        Assert.True(resumed.Run.FilesIndexed > 0);
    }

    [Fact]
    public async Task MarksFilesAboveTheSizeLimitAndBinaryFiles()
    {
        using var host = new TestHost();
        using var work = new TempDirectory();
        work.WriteFile("small.cs", "class Small {}");
        work.WriteFile("big.sql", new string('x', 5000));
        File.WriteAllBytes(work.Combine("data.json"), new byte[] { 0x7B, 0x00, 0x00, 0x7D });

        var result = await host.Get<IndexingService>().IndexAsync(new IndexingRequest(work.Path, new ScanOptions { MaxFileSizeBytes = 1000 }));
        var files = await host.Get<IIndexStore>().ListFilesAsync(result.Repository.Id);
        Assert.Equal(FileIndexStatus.Indexed, files.Single(f => f.RelativePath == "small.cs").Status);
        Assert.Equal(FileIndexStatus.TooLarge, files.Single(f => f.RelativePath == "big.sql").Status);
        Assert.Equal(FileIndexStatus.Binary, files.Single(f => f.RelativePath == "data.json").Status);
        Assert.Equal(2, result.Run.FilesSkipped);
    }

    [Fact]
    public async Task DeletingTheIndexKeepsTheSources()
    {
        using var host = new TestHost();
        using var work = new TempDirectory();
        var root = SampleRepository.CopyDemoShop(work);
        var store = host.Get<IIndexStore>();
        var result = await host.Get<IndexingService>().IndexAsync(new IndexingRequest(root, new ScanOptions()));

        await store.DeleteRepositoryAsync(result.Repository.Id);

        Assert.Null(await store.GetRepositoryAsync(result.Repository.Id));
        Assert.Empty(await store.ListFilesAsync(result.Repository.Id));
        Assert.True(File.Exists(Path.Combine(root, "Services", "OrderService.cs")));
    }

    [Fact]
    public void ResolvesSolutionFilesAsEntryPoints()
    {
        using var work = new TempDirectory();
        var solution = work.WriteFile("App.slnx", "<Solution />");
        work.WriteFile("notes.txt", "x");

        var (root, entry, name) = IndexingService.ResolveTarget(solution);
        Assert.Equal(Lcia.Application.PathUtilities.NormalizeRoot(work.Path), root);
        Assert.Equal(solution, entry);
        Assert.Equal("App", name);

        Assert.Throws<ArgumentException>(() => IndexingService.ResolveTarget(work.Combine("notes.txt")));
        Assert.Throws<DirectoryNotFoundException>(() => IndexingService.ResolveTarget(work.Combine("missing")));
    }
}
