using System.Data.Common;

namespace DemoShop.Data;

/// <summary>Crea connessioni al database (provider configurabile).</summary>
public interface IDbConnectionFactory
{
    DbConnection Create();
}

/// <summary>
/// Usa il provider ADO.NET registrato con il nome indicato in configurazione
/// (es. Microsoft.Data.SqlClient, registrato all'avvio con DbProviderFactories.RegisterFactory).
/// </summary>
public sealed class ConfiguredDbConnectionFactory(IConfiguration configuration) : IDbConnectionFactory
{
    public DbConnection Create()
    {
        var providerName = configuration["Database:ProviderInvariantName"] ?? "Microsoft.Data.SqlClient";
        var factory = DbProviderFactories.GetFactory(providerName);
        var connection = factory.CreateConnection()
            ?? throw new InvalidOperationException($"Il provider {providerName} non crea connessioni.");
        connection.ConnectionString = configuration.GetConnectionString("Shop");
        return connection;
    }
}
