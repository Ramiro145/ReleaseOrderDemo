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
            var shipment = new ShipmentDto
            {
                ShipmentId = Guid.NewGuid(),
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

        public async Task CancelShipmentAsync(Guid shipmentId, int orderId)
        {
            await _shipmentRepository.UpdateStatusAsync(shipmentId, "Canceled");
            await _orderRepository.UpdateStatusAsync(orderId, "ShippingCanceled");

            Console.WriteLine($"[Shipping] Shipment {shipmentId} canceled for order {orderId}");
        }
    }
}