using ReleaseOrder.Tests.Fakes;
using ReleaseOrder.Tests.Support;
using Xunit;

namespace ReleaseOrder.Tests;

/// <summary>
/// Prueba E del README sobre <c>ReleaseOrderWorkflow</c>: un error <b>reintentable</b>
/// agota los 3 <c>MaximumAttempts</c>; dos errores <b>no-reintentables</b> fallan al
/// primer intento — uno marcado por la Activity
/// (<c>ApplicationFailureException(nonRetryable: true)</c>), el otro por el workflow
/// (<c>RetryPolicy.NonRetryableErrorTypes</c>).
///
/// Los tres workflows fallan <b>antes</b> de <c>"Waiting for release decision"</c>, así
/// que el <c>drive</c> queda vacío y el auto time-skipping del servidor embebido salta
/// solo el <c>DelayAsync(5s)</c> inicial y el backoff de reintentos (1s + 2s).
/// </summary>
public class ReleaseOrderRetryPolicyTests
{
    private const int ProductId = 500;

    [Fact] // E.1 — timeout transitorio simulado: reintentable, agota los 3 intentos y compensa.
    public async Task PagoConFalloTransitorio_AgotaLosTresIntentosYCompensa()
    {
        const int orderId = 1007;

        var run = await ReleaseOrderTestEnvironment.RunAsync(
            seed: db => db.SeedOrder(orderId, ProductId, quantity: 2, amount: 999999m,
                                     address: "Calle 1", stock: 10),
            orderId: orderId,
            drive: _ => Task.CompletedTask);

        // ApplicationException genérica → Temporal reintenta hasta MaximumAttempts (3).
        Assert.Equal(3, run.History.AttemptsFor("ProcessPayment"));

        // El pago nunca se apiló como compensación; sí el inventario reservado antes.
        Assert.Contains("Final status: Compensated", run.Result);
        Assert.Equal("Compensated", run.FinalStatus);
        Assert.Contains(nameof(FakeOrderStateMachine.TryCancelInventoryAsync),
            run.Db.StateMachineCalls);

        // La compensación restauró el stock exactamente al valor sembrado.
        Assert.Equal(10, run.Db.Stock[ProductId]);
        Assert.Empty(run.Db.Shipments);
    }

    [Fact] // E.2 — gateway declina: no-reintentable decidido por la Activity. El contraste 3-vs-1 es la lección.
    public async Task PagoRechazado_NoReintentaYCompensa()
    {
        const int orderId = 1008;

        var run = await ReleaseOrderTestEnvironment.RunAsync(
            seed: db => db.SeedOrder(orderId, ProductId, quantity: 2, amount: 0m,
                                     address: "Calle 1", stock: 10),
            orderId: orderId,
            drive: _ => Task.CompletedTask);

        // ApplicationFailureException(nonRetryable: true): un solo intento, frente a los 3 de E.1.
        Assert.Equal(1, run.History.AttemptsFor("ProcessPayment"));
        Assert.Equal("PaymentDeclined", run.History.FailureErrorTypeFor("ProcessPayment"));

        // El inventario ya estaba apilado como compensación, así que termina "Compensated".
        Assert.Contains("Final status: Compensated", run.Result);
        Assert.Equal("Compensated", run.FinalStatus);
        Assert.Contains(nameof(FakeOrderStateMachine.TryCancelInventoryAsync),
            run.Db.StateMachineCalls);
        Assert.Equal(10, run.Db.Stock[ProductId]);
        Assert.Empty(run.Db.Shipments);
    }

    [Fact] // E.3 — sin stock: no-reintentable decidido por el WORKFLOW; el stack de compensación está vacío → "Failed".
    public async Task SinStock_NoReintentaYTerminaEnFailed()
    {
        const int orderId = 1009;

        var run = await ReleaseOrderTestEnvironment.RunAsync(
            seed: db => db.SeedOrder(orderId, ProductId, quantity: 5, amount: 100m,
                                     address: "Calle 1", stock: 1),
            orderId: orderId,
            drive: _ => Task.CompletedTask);

        // InventoryUnavailableException está en RetryPolicy.NonRetryableErrorTypes de
        // InventoryReserveOptions → el workflow, no la Activity, decide no reintentar.
        Assert.Equal(1, run.History.AttemptsFor("ReserveInventory"));
        Assert.Equal("InventoryUnavailableException",
            run.History.FailureErrorTypeFor("ReserveInventory"));

        // Falló en el primer paso reversible: nada apilado → rama "Failed" del ternario
        // de ReleaseOrderWorkFlow.cs, no "Compensated".
        Assert.Contains("Final status: Failed", run.Result);
        Assert.Equal("Failed", run.FinalStatus);
        Assert.Equal("Failed", run.Db.StatusHistory[^1]);

        // Stock intacto y ninguna compensación ejecutada.
        Assert.Equal(1, run.Db.Stock[ProductId]);
        Assert.DoesNotContain(nameof(FakeOrderStateMachine.TryCancelInventoryAsync),
            run.Db.StateMachineCalls);
        Assert.DoesNotContain(nameof(FakeOrderStateMachine.TryMarkPaymentRefundedAsync),
            run.Db.StateMachineCalls);
        Assert.Empty(run.Db.Shipments);
    }
}
