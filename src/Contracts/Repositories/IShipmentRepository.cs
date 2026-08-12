using Contracts.Dtos;

namespace Contracts.Repositories;

public interface IShipmentRepository
{
    Task InsertAsync(ShipmentDto shipment);
    Task UpdateStatusAsync(Guid shipmentId, string status);
}
