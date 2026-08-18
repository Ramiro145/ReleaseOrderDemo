using Contracts.Dtos;

namespace Contracts.Repositories;

public interface IShipmentRepository
{
    Task InsertAsync(ShipmentDto shipment);
    Task UpdateStatusAsync(int shipmentId, string status);
}
