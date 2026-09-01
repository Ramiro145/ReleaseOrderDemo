using System;
using System.Threading.Tasks;
using Temporalio.Activities;
using Contracts.Services;

namespace ReleaseOrderDemo.Activities
{
    /// <summary>
    /// Actividades de dominio de envíos.
    /// SRP: solo gestiona el despacho de órdenes.
    /// DIP: depende de IShippingService (abstracción). La idempotencia frente al
    /// at-least-once de Temporal la da IOrderStateMachine dentro de ShippingService.
    /// </summary>
    public class ShippingActivities
    {
        private readonly IShippingService _shippingService;

        public ShippingActivities(IShippingService shippingService)
        {
            _shippingService = shippingService;
        }

        [Activity]
        public async Task ShipOrderAsync(int orderId, string address)
        {
            var success = await _shippingService.ShipAsync(orderId, address);
            if (!success)
                throw new ApplicationException($"[Activity] Shipping failed for order {orderId}");

            Console.WriteLine($"[Activity] Order {orderId} shipped successfully");
        }
    }
}
