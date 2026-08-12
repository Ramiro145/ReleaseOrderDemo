using Contracts.Dtos;
using Contracts.Repositories;
using Temporalio.Activities;

namespace ReleaseOrderDemo.Activities;

/// <summary>
/// Lee la orden que utilizará el Workflow.
/// Se mantiene como Activity porque consultar SQL no es determinista.
/// </summary>
public class OrderLookupActivities
{
    private readonly IOrderRepository _orderRepository;

    public OrderLookupActivities(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    [Activity]
    public async Task<OrderDto> GetOrderAsync(int orderId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId.ToString());

        return order
            ?? throw new ApplicationException($"Order {orderId} was not found");
    }
}
