using System;
using System.Threading.Tasks;
using Temporalio.Activities;
using Contracts.Services;

namespace ReleaseOrderDemo.Activities
{
    /// <summary>
    /// Actividades de dominio de inventario.
    /// SRP: solo gestiona reservas y cancelaciones de stock.
    /// DIP: depende de IInventoryService (abstracción). La idempotencia frente al
    /// at-least-once de Temporal la da IOrderStateMachine dentro de InventoryService.
    /// </summary>
    public class InventoryActivities
    {
        private readonly IInventoryService _inventoryService;

        public InventoryActivities(IInventoryService inventoryService)
        {
            _inventoryService = inventoryService;
        }

        [Activity]
        public async Task<bool> CheckInventoryAsync(int productId, int quantity)
        {
            var available = await _inventoryService.CheckAvailabilityAsync(productId, quantity);
            Console.WriteLine($"[Activity] Inventory check for product {productId}, qty {quantity}: {(available ? "OK" : "Insufficient")}");
            return available;
        }

        [Activity]
        public async Task ReserveInventoryAsync(int orderId, int productId, int quantity)
        {
            var reserved = await _inventoryService.ReserveAsync(orderId, productId, quantity);
            if (!reserved)
                throw new InventoryUnavailableException($"[Activity] No stock available for order {orderId}");

            Console.WriteLine($"[Activity] Inventory reserved for order {orderId}");
        }

        [Activity]
        public async Task CancelInventoryAsync(int orderId, int productId, int quantity)
        {
            await _inventoryService.CancelAsync(orderId, productId, quantity);
            Console.WriteLine($"[Activity] Inventory reservation canceled for order {orderId}");
        }
    }
}
