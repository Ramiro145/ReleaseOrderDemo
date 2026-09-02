using Contracts.Dtos;

namespace ReleaseOrder.Tests.Fakes;

/// <summary>
/// "Base de datos" en memoria compartida por todos los fakes de un test, y a la vez
/// superficie de aserciones. En el sistema real tanto <c>OrderStatusActivities</c>
/// (vía <c>IOrderRepository.UpdateStatusAsync</c>) como <c>IOrderStateMachine</c>
/// escriben la MISMA columna <c>dbo.Orders.Status</c>: aquí también.
/// </summary>
public sealed class FakeOrderDatabase
{
    public Dictionary<int, OrderDto> Orders { get; } = new();

    /// <summary>ProductId → stock disponible.</summary>
    public Dictionary<int, int> Stock { get; } = new();

    /// <summary>OrderIds con una fila de envío (equivalente a dbo.Shipments).</summary>
    public List<int> Shipments { get; } = new();

    /// <summary>Cada transición de Status en orden de ocurrencia.</summary>
    public List<string> StatusHistory { get; } = new();

    /// <summary>Nombres de los <c>Try*Async</c> de <see cref="FakeOrderStateMachine"/> en orden.</summary>
    public List<string> StateMachineCalls { get; } = new();

    public OrderDto SeedOrder(
        int orderId,
        int productId,
        int quantity,
        decimal amount,
        string address,
        int stock,
        string status = "Created")
    {
        var order = new OrderDto
        {
            OrderId = orderId,
            OrderCode = $"ORD-{orderId}",
            ProductId = productId,
            Quantity = quantity,
            Amount = amount,
            Address = address,
            Status = status,
            CreatedAt = DateTime.UtcNow,
        };

        Orders[orderId] = order;
        Stock[productId] = stock;
        StatusHistory.Add(status);
        return order;
    }

    /// <summary>Avanza el Status de la orden y lo registra en <see cref="StatusHistory"/>.</summary>
    public void SetStatus(int orderId, string status)
    {
        if (Orders.TryGetValue(orderId, out var order))
            order.Status = status;

        StatusHistory.Add(status);
    }
}
