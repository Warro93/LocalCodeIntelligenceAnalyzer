using System.Data;
using System.Data.Common;
using System.Text.Json;
using DemoShop.Data;
using DemoShop.Models;

namespace DemoShop.Repositories;

/// <summary>Accesso ai dati degli ordini tramite stored procedure (ADO.NET).</summary>
public sealed class OrderRepository : IOrderRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public OrderRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<int> InsertOrderAsync(int customerId, IReadOnlyList<OrderLineRequest> lines, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.usp_Order_Create";
        command.CommandType = CommandType.StoredProcedure;
        AddParameter(command, "@CustomerId", DbType.Int32, customerId);
        AddParameter(command, "@LinesJson", DbType.String, JsonSerializer.Serialize(lines));

        var orderId = command.CreateParameter();
        orderId.ParameterName = "@OrderId";
        orderId.DbType = DbType.Int32;
        orderId.Direction = ParameterDirection.Output;
        command.Parameters.Add(orderId);

        await command.ExecuteNonQueryAsync(cancellationToken);
        return Convert.ToInt32(orderId.Value);
    }

    public async Task<Order?> GetByIdAsync(int orderId, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.usp_Order_GetById";
        command.CommandType = CommandType.StoredProcedure;
        AddParameter(command, "@OrderId", DbType.Int32, orderId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var order = new Order
        {
            OrderId = reader.GetInt32(0),
            CustomerId = reader.GetInt32(1),
            CustomerName = reader.GetString(2),
            Status = (OrderStatus)reader.GetByte(3),
            Total = reader.GetDecimal(4),
        };

        if (await reader.NextResultAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                order.Lines.Add(new OrderLine(reader.GetString(0), reader.GetInt32(1), reader.GetDecimal(2)));
            }
        }

        return order;
    }

    public async Task<bool> CancelOrderAsync(int orderId, string reason, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.usp_Order_Cancel";
        command.CommandType = CommandType.StoredProcedure;
        AddParameter(command, "@OrderId", DbType.Int32, orderId);
        AddParameter(command, "@Reason", DbType.String, reason);
        var affected = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(affected) > 0;
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

/// <summary>Verifica dei clienti con query parametrizzata in stringa.</summary>
public sealed class CustomerRepository(IDbConnectionFactory connectionFactory) : ICustomerRepository
{
    private const string ExistsSql = "SELECT COUNT(1) FROM dbo.Customers WHERE CustomerId = @CustomerId AND IsActive = 1";

    public async Task<bool> ExistsAsync(int customerId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = ExistsSql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@CustomerId";
        parameter.Value = customerId;
        command.Parameters.Add(parameter);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }
}
