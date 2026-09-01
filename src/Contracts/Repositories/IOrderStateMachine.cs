namespace Contracts.Repositories;

/// <summary>
/// Resultado de un intento de transición de estado sobre una orden.
/// </summary>
public enum StepOutcome
{
    /// <summary>La transición se aplicó ahora: efecto de dominio y Status escritos en esta llamada.</summary>
    Applied,

    /// <summary>La orden ya estaba en (o más allá de) el estado destino: reintento at-least-once detectado, sin efecto.</summary>
    AlreadyApplied,

    /// <summary>No hay stock suficiente para reservar.</summary>
    InsufficientStock,

    /// <summary>La orden no existe.</summary>
    OrderNotFound
}

/// <summary>
/// Idempotencia de las actividades de escritura de ReleaseOrder basada en dbo.Orders.Status
/// como máquina de estados: cada método aplica su efecto de dominio (Products.Stock,
/// dbo.Shipments) y avanza Status en una única transacción SQL, con lectura del estado
/// actual bajo UPDLOCK. Un reintento de Temporal siempre encuentra el Status ya avanzado
/// y devuelve AlreadyApplied sin duplicar el efecto — no requiere una tabla de ledger aparte.
/// </summary>
public interface IOrderStateMachine
{
    Task<StepOutcome> TryReserveInventoryAsync(int orderId, int productId, int quantity);
    Task<StepOutcome> TryCancelInventoryAsync(int orderId, int productId, int quantity);
    Task<StepOutcome> TryMarkPaymentProcessedAsync(int orderId);
    Task<StepOutcome> TryMarkPaymentRefundedAsync(int orderId);
    Task<StepOutcome> TryShipAsync(int orderId, string address);
}
