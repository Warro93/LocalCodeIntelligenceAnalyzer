using DemoShop.Models;
using DemoShop.Repositories;

namespace DemoShop.Services;

/// <summary>Coordina la creazione, la lettura e l'annullamento degli ordini.</summary>
public sealed class OrderService : IOrderService
{
    private readonly IOrderRepository _orders;
    private readonly ICustomerRepository _customers;
    private readonly ILogger<OrderService> _logger;

    public OrderService(IOrderRepository orders, ICustomerRepository customers, ILogger<OrderService> logger)
    {
        _orders = orders;
        _customers = customers;
        _logger = logger;
    }

    /// <summary>Valida la richiesta, verifica il cliente e inserisce l'ordine tramite stored procedure.</summary>
    public async Task<int> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Lines.Count == 0)
        {
            throw new ArgumentException("L'ordine deve contenere almeno una riga.", nameof(request));
        }

        if (!await _customers.ExistsAsync(request.CustomerId, cancellationToken))
        {
            throw new InvalidOperationException($"Cliente {request.CustomerId} inesistente o non attivo.");
        }

        var total = request.Lines.Sum(line => line.Quantity * line.UnitPrice);
        var orderId = await _orders.InsertOrderAsync(request.CustomerId, request.Lines, cancellationToken);
        _logger.LogInformation("Ordine {OrderId} creato, totale {Total}", orderId, total);
        return orderId;
    }

    public Task<Order?> GetOrderAsync(int orderId, CancellationToken cancellationToken) =>
        _orders.GetByIdAsync(orderId, cancellationToken);

    /// <summary>Annulla l'ordine se non è già stato annullato.</summary>
    public async Task<bool> CancelOrderAsync(int orderId, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Il motivo dell'annullamento è obbligatorio.", nameof(reason));
        }

        var cancelled = await _orders.CancelOrderAsync(orderId, reason, cancellationToken);
        _logger.LogInformation("Annullamento ordine {OrderId}: {Result}", orderId, cancelled);
        return cancelled;
    }
}
