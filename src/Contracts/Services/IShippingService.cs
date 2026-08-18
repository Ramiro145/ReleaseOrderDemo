namespace Contracts.Services;

public interface IShippingService
{
    Task<bool> ShipAsync(int orderId, string address);
    Task CancelShipmentAsync(int shipmentId, int orderId);
}
