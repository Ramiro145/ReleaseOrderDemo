namespace Contracts.Repositories;

public interface IShipmentRepository
{
    Task UpdateStatusAsync(int shipmentId, string status);
}
