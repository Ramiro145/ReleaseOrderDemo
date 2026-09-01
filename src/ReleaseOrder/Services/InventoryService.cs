using System.Threading.Tasks;
using Contracts.Services;
using Contracts.Repositories;

namespace ReleaseOrderDemo.Services
{
    public class InventoryService : IInventoryService
    {
        private readonly IProductRepository _productRepository;
        private readonly IOrderStateMachine _stateMachine;

        public InventoryService(IProductRepository productRepository, IOrderStateMachine stateMachine)
        {
            _productRepository = productRepository;
            _stateMachine = stateMachine;
        }

        // Verificar disponibilidad de inventario
        public async Task<bool> CheckAvailabilityAsync(int productId, int quantity)
        {
            var product = await _productRepository.GetByIdAsync(productId);
            return product != null && product.Stock >= quantity;
        }

        // Idempotente vía IOrderStateMachine: el decremento de stock y el avance de
        // Orders.Status a 'InventoryReserved' ocurren en una única transacción SQL;
        // un reintento de Temporal encuentra el Status ya avanzado y no vuelve a restar.
        public async Task<bool> ReserveAsync(int orderId, int productId, int quantity)
        {
            var outcome = await _stateMachine.TryReserveInventoryAsync(orderId, productId, quantity);
            return outcome is StepOutcome.Applied or StepOutcome.AlreadyApplied;
        }

        public async Task CancelAsync(int orderId, int productId, int quantity)
        {
            await _stateMachine.TryCancelInventoryAsync(orderId, productId, quantity);
        }
    }
}
