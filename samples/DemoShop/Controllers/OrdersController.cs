using DemoShop.Models;
using DemoShop.Services;
using Microsoft.AspNetCore.Mvc;

namespace DemoShop.Controllers;

/// <summary>API REST per la gestione degli ordini.</summary>
[ApiController]
[Route("api/orders")]
public sealed class OrdersController(IOrderService orderService, ILogger<OrdersController> logger) : ControllerBase
{
    /// <summary>Crea un nuovo ordine per un cliente esistente.</summary>
    [HttpPost]
    public async Task<ActionResult<int>> Create([FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Creazione ordine per il cliente {CustomerId}", request.CustomerId);
        var orderId = await orderService.CreateOrderAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = orderId }, orderId);
    }

    /// <summary>Restituisce un ordine con le sue righe.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Order>> GetById(int id, CancellationToken cancellationToken)
    {
        var order = await orderService.GetOrderAsync(id, cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }

    /// <summary>Annulla un ordine indicando il motivo.</summary>
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, [FromBody] CancelOrderRequest request, CancellationToken cancellationToken)
    {
        var cancelled = await orderService.CancelOrderAsync(id, request.Reason, cancellationToken);
        return cancelled ? NoContent() : NotFound();
    }
}
