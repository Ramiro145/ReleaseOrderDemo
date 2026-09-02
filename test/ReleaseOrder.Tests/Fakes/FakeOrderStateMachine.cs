using Contracts.Repositories;

namespace ReleaseOrder.Tests.Fakes;

/// <summary>
/// Reproduce en memoria la máquina de estados de <c>OrderStateMachine.cs</c>
/// (batches T-SQL con UPDLOCK). Contrato — de <c>specs/03-idempotencia-por-estado.md</c>
/// y del propio T-SQL. Si el T-SQL cambia, este fake cambia con él.
///
///  Paso              | Status destino    | "Ya aplicado" si Status ∈                              | Efecto
///  ----------------- | ----------------- | ----------------------------------------------------- | -----------------------
///  ReserveInventory  | InventoryReserved | InventoryReserved,PaymentProcessed,Completed,Shipped  | Stock -= qty (>= qty)
///  ProcessPayment    | PaymentProcessed  | PaymentProcessed,Completed,Shipped                    | (ninguno)
///  ShipOrder         | Shipped           | Shipped                                               | INSERT Shipments
///  CancelInventory   | InventoryCanceled | InventoryCanceled,Compensated,CompensationFailed      | Stock += qty
///  RefundPayment     | PaymentRefunded   | PaymentRefunded,InventoryCanceled,Compensated         | (ninguno)
/// </summary>
public sealed class FakeOrderStateMachine : IOrderStateMachine
{
    private readonly FakeOrderDatabase _db;

    public FakeOrderStateMachine(FakeOrderDatabase db) => _db = db;

    public Task<StepOutcome> TryReserveInventoryAsync(int orderId, int productId, int quantity)
    {
        _db.StateMachineCalls.Add(nameof(TryReserveInventoryAsync));

        if (!_db.Orders.TryGetValue(orderId, out var order))
            return Task.FromResult(StepOutcome.OrderNotFound);

        if (order.Status is "InventoryReserved" or "PaymentProcessed" or "Completed" or "Shipped")
            return Task.FromResult(StepOutcome.AlreadyApplied);

        if (!_db.Stock.TryGetValue(productId, out var stock) || stock < quantity)
            return Task.FromResult(StepOutcome.InsufficientStock);

        _db.Stock[productId] = stock - quantity;
        _db.SetStatus(orderId, "InventoryReserved");
        return Task.FromResult(StepOutcome.Applied);
    }

    public Task<StepOutcome> TryCancelInventoryAsync(int orderId, int productId, int quantity)
    {
        _db.StateMachineCalls.Add(nameof(TryCancelInventoryAsync));

        if (!_db.Orders.TryGetValue(orderId, out var order))
            return Task.FromResult(StepOutcome.OrderNotFound);

        if (order.Status is "InventoryCanceled" or "Compensated" or "CompensationFailed")
            return Task.FromResult(StepOutcome.AlreadyApplied);

        _db.Stock[productId] = _db.Stock.GetValueOrDefault(productId) + quantity;
        _db.SetStatus(orderId, "InventoryCanceled");

        // La sonda de replay (si el test la armó) lanza ACÁ, con el efecto ya aplicado y el
        // Status ya avanzado — la ventana exacta que el at-least-once de Temporal explota. El
        // reintento cae arriba en el chequeo de "InventoryCanceled" y devuelve AlreadyApplied,
        // así que el stock no se incrementa dos veces.
        _db.ThrowIfProbeArmed(nameof(TryCancelInventoryAsync));

        return Task.FromResult(StepOutcome.Applied);
    }

    public Task<StepOutcome> TryMarkPaymentProcessedAsync(int orderId)
    {
        _db.StateMachineCalls.Add(nameof(TryMarkPaymentProcessedAsync));

        if (!_db.Orders.TryGetValue(orderId, out var order))
            return Task.FromResult(StepOutcome.OrderNotFound);

        if (order.Status is "PaymentProcessed" or "Completed" or "Shipped")
            return Task.FromResult(StepOutcome.AlreadyApplied);

        _db.SetStatus(orderId, "PaymentProcessed");
        return Task.FromResult(StepOutcome.Applied);
    }

    public Task<StepOutcome> TryMarkPaymentRefundedAsync(int orderId)
    {
        _db.StateMachineCalls.Add(nameof(TryMarkPaymentRefundedAsync));

        if (!_db.Orders.TryGetValue(orderId, out var order))
            return Task.FromResult(StepOutcome.OrderNotFound);

        if (order.Status is "PaymentRefunded" or "InventoryCanceled" or "Compensated")
            return Task.FromResult(StepOutcome.AlreadyApplied);

        _db.SetStatus(orderId, "PaymentRefunded");
        return Task.FromResult(StepOutcome.Applied);
    }

    public Task<StepOutcome> TryShipAsync(int orderId, string address)
    {
        _db.StateMachineCalls.Add(nameof(TryShipAsync));

        if (!_db.Orders.TryGetValue(orderId, out var order))
            return Task.FromResult(StepOutcome.OrderNotFound);

        if (order.Status is "Shipped")
            return Task.FromResult(StepOutcome.AlreadyApplied);

        _db.Shipments.Add(orderId);
        _db.SetStatus(orderId, "Shipped");
        return Task.FromResult(StepOutcome.Applied);
    }
}
