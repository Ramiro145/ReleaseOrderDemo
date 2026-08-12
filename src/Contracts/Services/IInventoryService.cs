namespace Contracts.Services;

public interface IInventoryService
{
    Task<bool> CheckAvailabilityAsync(int productId, int quantity);
    Task<bool> ReserveAsync(int orderId, int productId, int quantity);
    Task CancelAsync(int orderId, int productId, int quantity);
}
