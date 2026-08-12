using System;
using System.Threading.Tasks;
using Contracts.Dtos;
using Contracts.Services;
using Contracts.Repositories;

namespace ReleaseOrderDemo.Services
{
    public class InventoryService : IInventoryService
    {
        private readonly IProductRepository _productRepository;

        public InventoryService(IProductRepository productRepository)
        {
            _productRepository = productRepository;
        }

        // Verificar disponibilidad de inventario
        public async Task<bool> CheckAvailabilityAsync(int productId, int quantity)
        {
            var product = await _productRepository.GetByIdAsync(productId);
            return product != null && product.Stock >= quantity;
        }


        public async Task<bool> ReserveAsync(int orderId, int productId, int quantity)
        {
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