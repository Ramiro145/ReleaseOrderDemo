using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Contracts.Dtos;
using Contracts.Services;
using Contracts.Repositories;

namespace ReleaseOrderDemo.Services
{
    public class ShippingService : IShippingService
    {
        private readonly IShipmentRepository _shipmentRepository;
        private readonly IOrderRepository _orderRepository;

        public ShippingService(IShipmentRepository shipmentRepository, IOrderRepository orderRepository)
        {
            _shipmentRepository = shipmentRepository;
            _orderRepository = orderRepository;
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

            var shipment = new ShipmentDto
            {
                OrderId = orderId,
                Address = address,
                Status = "Shipped",
                CreatedAt = DateTime.UtcNow
            };

            await _shipmentRepository.InsertAsync(shipment);
            await _orderRepository.UpdateStatusAsync(orderId, "Shipped");

            Console.WriteLine($"[Shipping] Order {orderId} shipped to {address}");
            return true;
        }

        public async Task CancelShipmentAsync(int shipmentId, int orderId)
        {
            await _shipmentRepository.UpdateStatusAsync(shipmentId, "Canceled");
            await _orderRepository.UpdateStatusAsync(orderId, "ShippingCanceled");

            Console.WriteLine($"[Shipping] Shipment {shipmentId} canceled for order {orderId}");
        }
    }
}