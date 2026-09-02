using Contracts.Dtos;
using Contracts.Repositories;

namespace ReleaseOrder.Tests.Fakes;

/// <summary>
/// Fake de <see cref="IOrderRepository"/> sobre <see cref="FakeOrderDatabase"/>.
/// Nota: <c>GetByIdAsync</c> recibe <c>string</c> — <c>OrderLookupActivities</c>
/// hace <c>orderId.ToString()</c>.
/// </summary>
public sealed class FakeOrderRepository : IOrderRepository
{
    private readonly FakeOrderDatabase _db;

    public FakeOrderRepository(FakeOrderDatabase db) => _db = db;

    public Task<OrderDto?> GetByIdAsync(string orderId)
    {
        var found = int.TryParse(orderId, out var id) && _db.Orders.TryGetValue(id, out var order)
            ? order
            : null;
        return Task.FromResult(found);
    }

    public Task AddAsync(OrderDto order)
    {
        _db.Orders[order.OrderId] = order;
        return Task.CompletedTask;
    }

    public Task UpdateStatusAsync(int orderId, string status)
    {
        _db.SetStatus(orderId, status);
        return Task.CompletedTask;
    }

    public Task<List<OrderDto>> GetAllAsync()
        => Task.FromResult(_db.Orders.Values.ToList());
}
