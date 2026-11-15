using Lcia.Application;

namespace Lcia.Tests;

/// <summary>DemoShop copiato e indicizzato una sola volta per classe di test.</summary>
public sealed class DemoShopFixture : IAsyncLifetime
{
    private TestHost? _host;
    private TempDirectory? _work;

    internal TestHost Host => _host ?? throw new InvalidOperationException("Fixture non inizializzata.");

    public string Root { get; private set; } = string.Empty;

    public long RepositoryId { get; private set; }

    public async Task InitializeAsync()
    {
        _host = new TestHost();
        _work = new TempDirectory("lcia-repo");
        Root = SampleRepository.CopyDemoShop(_work);
        var result = await _host.Get<IndexingService>().IndexAsync(new IndexingRequest(Root, new ScanOptions()));
        if (result.Run.Status != Lcia.Domain.ScanRunStatus.Completed)
        {
            throw new InvalidOperationException("Indicizzazione di DemoShop non riuscita: " + result.Run.Message);
        }

        RepositoryId = result.Repository.Id;
    }

    public Task DisposeAsync()
    {
        _host?.Dispose();
        _work?.Dispose();
        return Task.CompletedTask;
    }

    public string ReadSource(string relativePath) =>
        File.ReadAllText(Path.Combine(new[] { Root }.Concat(relativePath.Split('/')).ToArray()));
}
