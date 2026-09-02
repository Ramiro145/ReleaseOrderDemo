using Contracts.Repositories;

namespace ReleaseOrder.Tests.Fakes;

/// <summary>
/// Fake de <see cref="IShipmentRepository"/>. <c>ShippingService</c> lo recibe por
/// constructor pero, en el flujo del SAGA actual, solo <c>CancelShipmentAsync</c>
/// (no invocado por ningún workflow) lo usaría — se registra lo que llegue.
/// </summary>
public sealed class FakeShipmentRepository : IShipmentRepository
{
    private readonly FakeOrderDatabase _db;

    public FakeShipmentRepository(FakeOrderDatabase db) => _db = db;

    public Task UpdateStatusAsync(int shipmentId, string status)
    {
        _db.StateMachineCalls.Add($"Shipment.UpdateStatus({shipmentId},{status})");
        return Task.CompletedTask;
    }
}
