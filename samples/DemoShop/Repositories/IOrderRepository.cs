using DemoShop.Models;

namespace DemoShop.Repositories;

public interface IOrderRepository
{
    Task<int> InsertOrderAsync(int customerId, IReadOnlyList<OrderLineRequest> lines, CancellationToken cancellationToken);

    Task<Order?> GetByIdAsync(int orderId, CancellationToken cancellationToken);

    Task<bool> CancelOrderAsync(int orderId, string reason, CancellationToken cancellationToken);
}

public interface ICustomerRepository
{
    Task<bool> ExistsAsync(int customerId, CancellationToken cancellationToken);
}
