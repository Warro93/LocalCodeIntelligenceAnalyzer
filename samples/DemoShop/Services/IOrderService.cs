using DemoShop.Models;

namespace DemoShop.Services;

/// <summary>Casi d'uso degli ordini.</summary>
public interface IOrderService
{
    Task<int> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken);

    Task<Order?> GetOrderAsync(int orderId, CancellationToken cancellationToken);

    Task<bool> CancelOrderAsync(int orderId, string reason, CancellationToken cancellationToken);
}
