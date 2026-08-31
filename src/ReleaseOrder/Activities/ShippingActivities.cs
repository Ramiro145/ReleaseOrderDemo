using System;
using System.Threading.Tasks;
using Temporalio.Activities;
using Contracts.Services;
using Contracts.Repositories;

namespace ReleaseOrderDemo.Activities
{
    /// <summary>
    /// Actividades de dominio de envíos.
    /// SRP: solo gestiona el despacho de órdenes.
    /// DIP: depende de IShippingService e IOrderRepository (abstracciones).
    /// </summary>
    public class ShippingActivities
    {
        private readonly IShippingService _shippingService;
        private readonly IOrderRepository _orderRepository;
        private readonly IIdempotencyLedger _ledger;

        public ShippingActivities(
            IShippingService shippingService,
            IOrderRepository orderRepository,
            IIdempotencyLedger ledger)
        {
            _shippingService = shippingService;
            _orderRepository = orderRepository;
            _ledger = ledger;
        }

        [Activity]
        public async Task ShipOrderAsync(int orderId, string address)
        {
            await IdempotentActivity.RunAsync(_ledger, orderId, async () =>
            {
                var success = await _shippingService.ShipAsync(orderId, address);
                if (!success)
                    throw new ApplicationException($"[Activity] Shipping failed for order {orderId}");

                await _orderRepository.UpdateStatusAsync(orderId, "Shipped");
                Console.WriteLine($"[Activity] Order {orderId} shipped successfully");
            });
        }
    }
}
