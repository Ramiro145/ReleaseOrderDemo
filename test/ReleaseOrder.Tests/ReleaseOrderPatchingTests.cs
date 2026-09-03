using Contracts.Workflows;
using ReleaseOrder.Tests.Support;
using Xunit;

namespace ReleaseOrder.Tests;

/// <summary>
/// Prueba H del README — versionado de código de Workflow con
/// <c>Workflow.Patched("audit-before-decision")</c> (spec 04). Con time-skipping,
/// contra el mismo arnés que las Pruebas A–F.
///
/// <para>
/// El arnés arranca siempre con el código nuevo, así que <c>Patched</c> devuelve
/// <c>true</c>: acá se verifica el lado "ejecución nueva" del patch (corre el paso
/// de auditoría y escribe el marker). El lado "ejecución vieja en vuelo" (Patched
/// devuelve <c>false</c>, sin <c>NonDeterminismError</c>) se prueba a mano con el
/// stack en Docker — ver README Prueba H.
/// </para>
/// </summary>
public class ReleaseOrderPatchingTests
{
    private const int ProductId = 500;

    [Fact] // H.a — ejecución nueva: el paso parcheado corre y deja el marker "Version".
    public async Task EjecucionNueva_CorrePasoDeAuditoriaYEscribeMarker()
    {
        const int orderId = 1301;

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

        // Workflow.Patched escribió exactamente un marker de patch para este patchId.
        // El sdk-core lo nombra "core_patch" (los SDK Go/Java legacy usan "Version").
        Assert.Equal(1, run.History.CountMarkers("core_patch"));

        // El paso nuevo (AuditActivities.RecordAwaitingDecisionAsync) efectivamente corrió.
        Assert.True(run.History.AttemptsFor("RecordAwaitingDecisionAsync") >= 1,
            "La ejecución nueva debe agendar y completar RecordAwaitingDecisionAsync.");
    }

    [Fact] // H.b — el paso parcheado no altera el resultado de negocio (mismo string y estados que la Prueba A).
    public async Task PasoDeAuditoria_NoCambiaElResultadoDeNegocio()
    {
        const int orderId = 1302;

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

        // Mismo resultado observable que la Prueba A (ReleaseOrderWorkflowTests).
        Assert.Contains("released successfully", run.Result);
        Assert.Contains("shipped to Calle 1", run.Result);
        Assert.Equal(
            new[] { "Created", "InventoryReserved", "PaymentProcessed", "Completed", "Shipped" },
            run.Db.StatusHistory);
        Assert.Equal(8, run.Db.Stock[ProductId]);
        Assert.Equal(new[] { orderId }, run.Db.Shipments);
        Assert.Equal("Completed", run.FinalStatus);
    }
}
