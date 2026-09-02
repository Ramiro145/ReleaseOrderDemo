using Contracts.Workflows;
using ReleaseOrder.Tests.Fakes;
using ReleaseOrder.Tests.Support;
using Xunit;

namespace ReleaseOrder.Tests;

/// <summary>
/// Pruebas A y B del README (Signal aprobada / Signal rechazada) sobre
/// <c>ReleaseOrderWorkflow</c>, con time-skipping. Sin Docker ni SQL Server:
/// las Activities y los Services corren reales, solo el borde SQL es fake.
/// </summary>
public class ReleaseOrderWorkflowTests
{
    private const int ProductId = 500;

    [Fact] // Prueba A + C.1 — Signal aprobada: completa la orden y despacha vía Child Workflow.
    public async Task SignalAprobada_CompletaOrdenYDespacha()
    {
        const int orderId = 1001;

        var run = await ReleaseOrderTestEnvironment.RunAsync(
            seed: db => db.SeedOrder(orderId, ProductId, quantity: 2, amount: 100m,
                                     address: "Calle 1", stock: 10),
            orderId: orderId,
            drive: async ctx =>
            {
                await ctx.WaitForStatusAsync("Waiting for release decision");
                await ctx.Handle.SignalAsync(wf =>
                    wf.SubmitReleaseDecisionAsync(new ReleaseDecision(true, "Approved for release")));
            });

        // El Child Workflow corrió y su resultado se concatenó al del parent.
        Assert.Contains("released successfully", run.Result);
        Assert.Contains("shipped to Calle 1", run.Result);

        // Recorrido completo de Orders.Status.
        Assert.Equal(
            new[] { "Created", "InventoryReserved", "PaymentProcessed", "Completed", "Shipped" },
            run.Db.StatusHistory);

        // Stock decrementado EXACTAMENTE Quantity (no el doble): idempotencia.
        Assert.Equal(8, run.Db.Stock[ProductId]);

        // Una sola fila de envío para esta orden.
        Assert.Equal(new[] { orderId }, run.Db.Shipments);

        // Camino feliz: no se disparó ninguna compensación.
        Assert.DoesNotContain(nameof(FakeOrderStateMachine.TryCancelInventoryAsync),
            run.Db.StateMachineCalls);
        Assert.DoesNotContain(nameof(FakeOrderStateMachine.TryMarkPaymentRefundedAsync),
            run.Db.StateMachineCalls);

        Assert.Equal("Completed", run.FinalStatus);
    }

    [Fact] // Prueba B — Signal rechazada: compensación LIFO (refund antes de cancel).
    public async Task SignalRechazada_CompensaEnOrdenLIFO()
    {
        const int orderId = 1002;

        var run = await ReleaseOrderTestEnvironment.RunAsync(
            seed: db => db.SeedOrder(orderId, ProductId, quantity: 2, amount: 100m,
                                     address: "Calle 1", stock: 10),
            orderId: orderId,
            drive: async ctx =>
            {
                await ctx.WaitForStatusAsync("Waiting for release decision");
                await ctx.Handle.SignalAsync(wf =>
                    wf.SubmitReleaseDecisionAsync(
                        new ReleaseDecision(false, "Manual review rejected the release")));
            });

        // El workflow NUNCA falla: el catch devuelve el resultado como string.
        Assert.Contains("Final status: Compensated", run.Result);

        // LIFO: la compensación del pago (última en apilarse) corre antes que la de inventario.
        var calls = run.Db.StateMachineCalls;
        var refundIdx = calls.IndexOf(nameof(FakeOrderStateMachine.TryMarkPaymentRefundedAsync));
        var cancelIdx = calls.IndexOf(nameof(FakeOrderStateMachine.TryCancelInventoryAsync));
        Assert.True(refundIdx >= 0 && cancelIdx >= 0, "Ambas compensaciones deben ejecutarse.");
        Assert.True(refundIdx < cancelIdx,
            $"El refund del pago debe preceder a la cancelación de inventario (LIFO). calls=[{string.Join(", ", calls)}]");

        // Stock restaurado al valor original: la compensación no lo deja por encima ni por debajo.
        Assert.Equal(10, run.Db.Stock[ProductId]);

        // No hubo envío.
        Assert.Empty(run.Db.Shipments);

        Assert.Equal("Compensated", run.Db.StatusHistory[^1]);
        Assert.Equal("Compensated", run.FinalStatus);
    }
}
