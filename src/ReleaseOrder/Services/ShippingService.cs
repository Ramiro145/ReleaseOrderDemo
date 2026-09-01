using System;
using System.Threading.Tasks;
using Contracts.Services;
using Contracts.Repositories;

namespace ReleaseOrderDemo.Services
{
    public class ShippingService : IShippingService
    {
        private readonly IShipmentRepository _shipmentRepository;
        private readonly IOrderRepository _orderRepository;
        private readonly IOrderStateMachine _stateMachine;

        public ShippingService(
            IShipmentRepository shipmentRepository,
            IOrderRepository orderRepository,
            IOrderStateMachine stateMachine)
        {
            _shipmentRepository = shipmentRepository;
            _orderRepository = orderRepository;
            _stateMachine = stateMachine;
        }

        public async Task<bool> ShipAsync(int orderId, string address)
        {
            // Valor mágico para la demo (igual que Amount en PaymentActivities):
            // una dirección que contiene "FAIL" simula un despacho fallido, para
            // observar cómo el fallo de un Child Workflow (agotados sus reintentos)
            // dispara la compensación LIFO del ReleaseOrderWorkflow padre.
            if (address?.Contains("FAIL", StringComparison.OrdinalIgnoreCase) == true)
            {
                Console.WriteLine($"[Shipping] Simulated shipping failure for order {orderId} (address contains 'FAIL')");
                return false;
            }

            // Idempotente vía IOrderStateMachine: el INSERT en Shipments y el avance de
            // Orders.Status a 'Shipped' ocurren en una única transacción SQL; un reintento
            // de Temporal encuentra el Status ya avanzado y no vuelve a insertar.
            var outcome = await _stateMachine.TryShipAsync(orderId, address!);
            if (outcome is not (StepOutcome.Applied or StepOutcome.AlreadyApplied))
                return false;

            Console.WriteLine($"[Shipping] Order {orderId} shipped to {address}");
            return true;
        }

        public async Task CancelShipmentAsync(int shipmentId, int orderId)
        {
            await _shipmentRepository.UpdateStatusAsync(shipmentId, "Canceled");
            // No forma parte del SAGA actual (ningún workflow invoca este método), así que
            // se mantiene fuera de la máquina de estados de IOrderStateMachine.
            await _orderRepository.UpdateStatusAsync(orderId, "ShippingCanceled");

            Console.WriteLine($"[Shipping] Shipment {shipmentId} canceled for order {orderId}");
        }
    }
}
