using System;
using System.Threading.Tasks;
using Temporalio.Activities;
using Contracts.Services;
using Contracts.Repositories;

namespace ReleaseOrderDemo.Activities
{
    /// <summary>
    /// Actividades de dominio de inventario.
    /// SRP: solo gestiona reservas y cancelaciones de stock.
    /// DIP: depende de IInventoryService e IOrderRepository (abstracciones).
    /// </summary>
    public class InventoryActivities
    {
        private readonly IInventoryService _inventoryService;
        private readonly IOrderRepository _orderRepository;
        private readonly IIdempotencyLedger _ledger;

        public InventoryActivities(
            IInventoryService inventoryService,
            IOrderRepository orderRepository,
            IIdempotencyLedger ledger)
        {
            _inventoryService = inventoryService;
            _orderRepository = orderRepository;
            _ledger = ledger;
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
            await IdempotentActivity.RunAsync(_ledger, orderId, async () =>
            {
                var reserved = await _inventoryService.ReserveAsync(orderId, productId, quantity);
                if (!reserved)
                    throw new InventoryUnavailableException($"[Activity] No stock available for order {orderId}");

                await _orderRepository.UpdateStatusAsync(orderId, "InventoryReserved");
                Console.WriteLine($"[Activity] Inventory reserved for order {orderId}");
            });
        }

        [Activity]
        public async Task CancelInventoryAsync(int orderId, int productId, int quantity)
        {
            await IdempotentActivity.RunAsync(_ledger, orderId, async () =>
            {
                await _inventoryService.CancelAsync(orderId, productId, quantity);
                await _orderRepository.UpdateStatusAsync(orderId, "InventoryCanceled");
                Console.WriteLine($"[Activity] Inventory reservation canceled for order {orderId}");
            });
        }
    }
}
