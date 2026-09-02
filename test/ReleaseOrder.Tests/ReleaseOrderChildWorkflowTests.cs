using Contracts.Workflows;
using ReleaseOrder.Tests.Fakes;
using ReleaseOrder.Tests.Support;
using Temporalio.Api.Enums.V1;
using Xunit;

namespace ReleaseOrder.Tests;

/// <summary>
/// Prueba C del README: <c>ShippingWorkflow</c> es una Workflow Execution <b>independiente</b>
/// (Id propio <c>shipping-order-{orderId}</c>, Event History propio, <c>RetryPolicy</c>
/// propio). C.2 — cuando agota sus reintentos y falla, el SAGA padre lo trata igual que un
/// fallo de Activity propia: mismo <c>catch</c>, misma compensación LIFO. C.1 — camino feliz.
/// </summary>
public class ReleaseOrderChildWorkflowTests
{
    private const int ProductId = 500;

    [Fact] // C.2 — el hijo agota SUS reintentos, falla, y el padre compensa como ante cualquier fallo.
    public async Task AddressConFAIL_ChildAgotaReintentosYParentCompensa()
    {
        const int orderId = 1010;

        var run = await ReleaseOrderTestEnvironment.RunAsync(
            seed: db => db.SeedOrder(orderId, ProductId, quantity: 2, amount: 100m,
                                     address: "Calle FAIL 123", stock: 10),
            orderId: orderId,
            drive: async ctx =>
            {
                await ctx.WaitForStatusAsync("Waiting for release decision");
                await ctx.SubmitDecisionSignalAsync(new ReleaseDecision(true, "Approved for release"));
            });

        // El hijo, como ejecución aparte: falló, y los 3 reintentos son SUYOS (su propio
        // RetryPolicy en ShippingWorkflow.cs) — se cuentan en SU historia, no en la del padre.
        Assert.NotNull(run.Child);
        Assert.Equal(WorkflowExecutionStatus.Failed, run.Child!.Status);
        Assert.Equal(3, run.Child.History.AttemptsFor("ShipOrder"));

        // El padre ve el fallo del hijo como un evento de Child Workflow, no de Activity.
        Assert.True(run.History.ContainsEventType(EventType.ChildWorkflowExecutionFailed));

        // El SAGA reaccionó igual que ante un fallo de Activity propia: compensación LIFO
        // completa. El "Completed" previo (la orden ya estaba marcada completa antes de
        // despachar) también se revierte.
        Assert.Contains("Final status: Compensated", run.Result);
        Assert.Equal("Compensated", run.FinalStatus);
        Assert.Equal(
            new[] { "Created", "InventoryReserved", "PaymentProcessed", "Completed",
                    "PaymentRefunded", "InventoryCanceled", "Compensated" },
            run.Db.StatusHistory);

        // Stock restaurado y sin envío: ShipAsync sale antes por el "FAIL", TryShipAsync nunca se aplicó.
        Assert.Equal(10, run.Db.Stock[ProductId]);
        Assert.Empty(run.Db.Shipments);
    }

    [Fact] // C.1 — camino feliz: el hijo corre con su Id propio y completa en un solo intento.
    public async Task ChildWorkflow_CorreConSuPropioIdYCompleta()
    {
        const int orderId = 1011;

        var run = await ReleaseOrderTestEnvironment.RunAsync(
            seed: db => db.SeedOrder(orderId, ProductId, quantity: 2, amount: 100m,
                                     address: "Calle 1", stock: 10),
            orderId: orderId,
            drive: async ctx =>
            {
                await ctx.WaitForStatusAsync("Waiting for release decision");
                await ctx.SubmitDecisionSignalAsync(new ReleaseDecision(true, "Approved for release"));
            });

        // El hijo tiene Id y ciclo de vida propios.
        Assert.NotNull(run.Child);
        Assert.Equal($"shipping-order-{orderId}", run.Child!.WorkflowId);
        Assert.Equal(WorkflowExecutionStatus.Completed, run.Child.Status);
        Assert.Equal(1, run.Child.History.AttemptsFor("ShipOrder")); // contraste con los 3 del test anterior

        // El padre inició y vio completar al hijo.
        Assert.True(run.History.ContainsEventType(EventType.StartChildWorkflowExecutionInitiated));
        Assert.True(run.History.ContainsEventType(EventType.ChildWorkflowExecutionCompleted));

        Assert.Equal("Completed", run.FinalStatus);
        Assert.Equal(new[] { orderId }, run.Db.Shipments);
    }
}
