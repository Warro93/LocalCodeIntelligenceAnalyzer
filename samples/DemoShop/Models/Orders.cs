namespace DemoShop.Models;

public enum OrderStatus : byte
{
    Created = 0,
    Confirmed = 1,
    Cancelled = 2,
}

public sealed class Order
{
    public int OrderId { get; init; }
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public OrderStatus Status { get; init; }
    public decimal Total { get; init; }
    public List<OrderLine> Lines { get; } = [];
}

public sealed record OrderLine(string ProductCode, int Quantity, decimal UnitPrice);

public sealed record OrderLineRequest(string ProductCode, int Quantity, decimal UnitPrice);

public sealed record CreateOrderRequest(int CustomerId, IReadOnlyList<OrderLineRequest> Lines);

public sealed record CancelOrderRequest(string Reason);
