using Contracts.Workflows;
using ReleaseOrder.Tests.Fakes;
using ReleaseOrder.Tests.Support;
using Xunit;

namespace ReleaseOrder.Tests;

/// <summary>
/// Prueba F del README: la garantía <b>at-least-once</b> de Temporal reintenta una Activity
/// que aplicó su efecto pero se cayó antes de reportar completitud. <c>ReleaseOrder</c> lo
/// resuelve con <c>Orders.Status</c> como marca de idempotencia, que avanza en la misma
/// transacción que el efecto (<c>specs/03-idempotencia-por-estado.md</c>): el reintento
/// encuentra el Status ya avanzado y no duplica nada.
/// </summary>
public class ReleaseOrderIdempotencyTests
{
    private const int ProductId = 500;

    [Fact] // F.1 + F.2 — replay probe en el camino feliz: el pago se contabiliza una sola vez.
    public async Task ReplayProbeDePago_AplicaElEfectoUnaSolaVez()
    {
        const int orderId = 1012;

        var run = await ReleaseOrderTestEnvironment.RunAsync(
            // Amount 888888 = ReplayProbeAmount: ProcessPaymentAsync avanza el Status y
            // ACTO SEGUIDO lanza (reintentable) solo en el intento 1.
            seed: db => db.SeedOrder(orderId, ProductId, quantity: 2, amount: 888888m,
                                     address: "Calle 1", stock: 10),
            orderId: orderId,
            drive: async ctx =>
            {
                await ctx.WaitForStatusAsync("Waiting for release decision");
                await ctx.SubmitDecisionSignalAsync(new ReleaseDecision(true, "Approved for release"));
            });

        // Hubo reintento real, y la máquina de estados se consultó dos veces...
        Assert.Equal(2, run.History.AttemptsFor("ProcessPayment"));
        Assert.Equal(2, run.Db.StateMachineCalls.Count(
            c => c == nameof(FakeOrderStateMachine.TryMarkPaymentProcessedAsync)));

        // ...pero el efecto (avance a "PaymentProcessed") se aplicó una sola vez. Ese par
        // de aserciones juntas es la lección de F.1.
        Assert.Single(run.Db.StatusHistory, s => s == "PaymentProcessed");

        // F.2: el stock bajó EXACTAMENTE Quantity, no el doble; una sola fila de envío.
        Assert.Equal(8, run.Db.Stock[ProductId]);
        Assert.Equal(new[] { orderId }, run.Db.Shipments);
        Assert.Equal("Completed", run.FinalStatus);
        Assert.Equal(
            new[] { "Created", "InventoryReserved", "PaymentProcessed", "Completed", "Shipped" },
            run.Db.StatusHistory);
    }

    [Fact] // F.3 — compensación reintentada: el stock no se restaura dos veces.
    public async Task CompensacionReintentada_NoRestauraElStockDosVeces()
    {
        const int orderId = 1013;

        var run = await ReleaseOrderTestEnvironment.RunAsync(
            seed: db =>
            {
                db.SeedOrder(orderId, ProductId, quantity: 2, amount: 100m,
                             address: "Calle 1", stock: 10);
                // La cancelación de inventario aplica su efecto (stock += qty) y después
                // falla una vez, forzando el retry de CompensationOptions.
                db.FailAfterEffect(nameof(FakeOrderStateMachine.TryCancelInventoryAsync));
            },
            orderId: orderId,
            drive: async ctx =>
            {
                await ctx.WaitForStatusAsync("Waiting for release decision");
                await ctx.SubmitDecisionSignalAsync(
                    new ReleaseDecision(false, "Manual review rejected the release"));
            });

        // El RetryPolicy de CompensationOptions (5 intentos) reaplicó la compensación.
        Assert.Equal(2, run.History.AttemptsFor("CancelInventory"));
        Assert.Equal(2, run.Db.StateMachineCalls.Count(
            c => c == nameof(FakeOrderStateMachine.TryCancelInventoryAsync)));
        Assert.Single(run.Db.StatusHistory, s => s == "InventoryCanceled");

        // La lección: sin la guarda de "ya aplicado" en la máquina de estados, el segundo
        // intento volvería a sumar Quantity y el stock quedaría en 12. La guarda lo deja
        // exactamente en el valor original.
        Assert.Equal(10, run.Db.Stock[ProductId]);

        // El reintento tuvo éxito → compensationFailures == 0 → "Compensated", no "CompensationFailed".
        Assert.Contains("Final status: Compensated", run.Result);
        Assert.Equal("Compensated", run.FinalStatus);
        Assert.Equal(
            new[] { "Created", "InventoryReserved", "PaymentProcessed",
                    "PaymentRefunded", "InventoryCanceled", "Compensated" },
            run.Db.StatusHistory);
    }
}
