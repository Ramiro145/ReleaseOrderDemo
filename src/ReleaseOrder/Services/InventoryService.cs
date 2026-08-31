using System;
using System.Threading.Tasks;
using Contracts.Dtos;
using Contracts.Services;
using Contracts.Repositories;

namespace ReleaseOrderDemo.Services
{
    public class InventoryService : IInventoryService
    {
        // Estados en los que la reserva de inventario para la orden ya se aplicó:
        // si la orden está en alguno de ellos, un reintento de ReserveAsync no debe
        // volver a restar stock (defensa en profundidad para la ventana no atómica
        // entre la escritura de dominio y la del ledger de idempotencia).
        private static readonly string[] AlreadyReservedStatuses =
            { "InventoryReserved", "PaymentProcessed", "Completed", "Shipped" };

        private readonly IProductRepository _productRepository;
        private readonly IOrderRepository _orderRepository;

        public InventoryService(IProductRepository productRepository, IOrderRepository orderRepository)
        {
            _productRepository = productRepository;
            _orderRepository = orderRepository;
        }

        // Verificar disponibilidad de inventario
        public async Task<bool> CheckAvailabilityAsync(int productId, int quantity)
        {
            var product = await _productRepository.GetByIdAsync(productId);
            return product != null && product.Stock >= quantity;
        }


        public async Task<bool> ReserveAsync(int orderId, int productId, int quantity)
        {
            // Guarda de estado natural: si la orden ya pasó por la reserva, no restar de nuevo.
            var order = await _orderRepository.GetByIdAsync(orderId.ToString());
            if (order != null && Array.IndexOf(AlreadyReservedStatuses, order.Status) >= 0)
            {
                Console.WriteLine($"[Inventory] Order {orderId} already in '{order.Status}'; skipping re-reservation");
                return true;
            }

            var product = await _productRepository.GetByIdAsync(productId);
            if (product == null || !product.IsActive || product.Stock < quantity)
            {
                Console.WriteLine($"[Inventory] Cannot reserve {quantity} units of {productId} for order {orderId}");
                return false;
            }

            var newStock = product.Stock - quantity;
            await _productRepository.UpdateStockAsync(productId, newStock);

            Console.WriteLine($"[Inventory] Reserved {quantity} units of {productId} for order {orderId}. Remaining stock: {newStock}");
            return true;
        }

        public async Task CancelAsync(int orderId, int productId, int quantity)
        {
            // Guarda de estado natural: si la cancelación ya se aplicó, no volver a sumar.
            var order = await _orderRepository.GetByIdAsync(orderId.ToString());
            if (order != null && order.Status == "InventoryCanceled")
            {
                Console.WriteLine($"[Inventory] Order {orderId} already in 'InventoryCanceled'; skipping re-restore");
                return;
            }

            var product = await _productRepository.GetByIdAsync(productId);
            if (product != null)
            {
                var newStock = product.Stock + quantity;
                await _productRepository.UpdateStockAsync(productId, newStock);

                Console.WriteLine($"[Inventory] Reservation canceled for order {orderId}. Restored {quantity} units of {productId}. New stock: {newStock}");
            }
        }
    }
}