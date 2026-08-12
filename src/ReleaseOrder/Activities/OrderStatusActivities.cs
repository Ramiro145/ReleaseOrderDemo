using System;
using System.Threading.Tasks;
using Temporalio.Activities;
using Contracts.Repositories;

namespace ReleaseOrderDemo.Activities
{
    /// <summary>
    /// Actividades de actualización de estado de orden.
    /// SRP: única responsabilidad — actualizar el estado en persistencia.
    /// DIP: depende de IOrderRepository (abstracción).
    /// </summary>
    public class OrderStatusActivities
    {
        private readonly IOrderRepository _orderRepository;

        public OrderStatusActivities(IOrderRepository orderRepository)
        {
            _orderRepository = orderRepository;
        }

        [Activity]
        public async Task UpdateOrderStatusAsync(int orderId, string status)
        {
            await _orderRepository.UpdateStatusAsync(orderId, status);
            Console.WriteLine($"[Activity] Order {orderId} status updated to {status}");
        }
    }
}
