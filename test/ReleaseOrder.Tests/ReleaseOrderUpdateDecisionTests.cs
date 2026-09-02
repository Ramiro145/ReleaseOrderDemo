using Contracts.Workflows;
using ReleaseOrder.Tests.Fakes;
using ReleaseOrder.Tests.Support;
using Xunit;

namespace ReleaseOrder.Tests;

/// <summary>
/// Prueba D del README — el <c>[WorkflowUpdate]</c> como segunda vía hacia el MISMO estado de
/// decisión que ya cubren las Pruebas A/B con Signal (<see cref="ReleaseOrderWorkflowTests"/>),
/// más la regla de idempotencia "la primera decisión gana" compartida por ambas vías
/// (<c>ReleaseOrderWorkFlow.cs:170-210</c>). El contraste que ilustra:
///   - la Signal no devuelve resultado de negocio; el Update sí, y síncronamente;
///   - la Signal siempre se acepta; el Update lo puede rechazar el
///     <c>[WorkflowUpdateValidator]</c>, sin dejar evento en el Event History.
///
/// ⚠️ Dependencia frágil: <see cref="UpdateRechazadoPorValidador_CuandoNoEstaEsperando"/> vive de
/// la ventana que da el <c>Workflow.DelayAsync(5s)</c> de <c>ReleaseOrderWorkFlow.cs:64</c>
/// (marcado en el código como "delay de prueba… revertir luego de probar"). Si ese delay se
/// quita, ese test hay que replantearlo.
/// </summary>
public class ReleaseOrderUpdateDecisionTests
{
    private const int ProductId = 500;

    [Fact] // D.1 — Update aprobado: resultado de negocio síncrono (lo que la Signal no da) y orden completada.
    public async Task UpdateAprobado_DevuelveResultadoSincronoYCompleta()
    {
        const int orderId = 1003;
        string? updateResult = null;

        var run = await ReleaseOrderTestEnvironment.RunAsync(
            seed: db => db.SeedOrder(orderId, ProductId, quantity: 2, amount: 100m,
                                     address: "Calle 1", stock: 10),
            orderId: orderId,
            drive: async ctx =>
            {
                await ctx.WaitForStatusAsync("Waiting for release decision");
                updateResult = await ctx.SubmitDecisionUpdateAsync(
                    new ReleaseDecision(true, "Approved via Update"));
            });

        // La diferencia con la Signal: el Update devuelve un string de negocio, de inmediato.
        Assert.Equal("Decision accepted: order will be completed.", updateResult);

        Assert.Contains("released successfully", run.Result);
        Assert.Contains("shipped to Calle 1", run.Result);

        Assert.Equal(
            new[] { "Created", "InventoryReserved", "PaymentProcessed", "Completed", "Shipped" },
            run.Db.StatusHistory);
        Assert.Equal(new[] { orderId }, run.Db.Shipments);
        Assert.Equal("Completed", run.FinalStatus);
    }

    [Fact] // D.2 — El validador del Update rechaza mientras el estado no es "Waiting for release decision".
    public async Task UpdateRechazadoPorValidador_CuandoNoEstaEsperando()
    {
        const int orderId = 1004;

        var run = await ReleaseOrderTestEnvironment.RunAsync(
            seed: db => db.SeedOrder(orderId, ProductId, quantity: 2, amount: 100m,
                                     address: "Calle 1", stock: 10),
            orderId: orderId,
            drive: async ctx =>
            {
                // Sin esperar el status: el workflow está frenado en el Workflow.DelayAsync(5s)
                // inicial y _status es "Loading order". El auto time-skipping queda suspendido
                // mientras hay una llamada de cliente en vuelo, así que la ventana no depende
                // del reloj real.
                var ex = await ctx.ExpectUpdateRejectedAsync(
                    new ReleaseDecision(false, "demasiado temprano"));
                Assert.Contains("not waiting for a decision", ex.InnerException!.Message);

                // El rechazo no dejó estado pegado: la Signal aprobada destraba y la corrida
                // cierra limpia en "Completed".
                await ctx.WaitForStatusAsync("Waiting for release decision");
                await ctx.SubmitDecisionSignalAsync(new ReleaseDecision(true, "Approved after reject"));
            });

        Assert.Contains("released successfully", run.Result);
        Assert.Equal(new[] { orderId }, run.Db.Shipments);
        Assert.Equal("Completed", run.FinalStatus);
    }

    [Fact] // D.3 — Signals duplicadas con decisiones opuestas: gana la primera (rechazo) → compensa.
    public async Task SignalDuplicada_PrimeraGana()
    {
        const int orderId = 1005;

        var run = await ReleaseOrderTestEnvironment.RunAsync(
            seed: db => db.SeedOrder(orderId, ProductId, quantity: 2, amount: 100m,
                                     address: "Calle 1", stock: 10),
            orderId: orderId,
            drive: async ctx =>
            {
                // Ambas Signals durante la ventana del Workflow.DelayAsync(5s) inicial: se
                // bufferizan en el servidor y se entregan juntas en el siguiente workflow task,
                // sin carrera contra el cierre del workflow.
                await ctx.SubmitDecisionSignalAsync(new ReleaseDecision(false, "rechazo original"));
                await ctx.SubmitDecisionSignalAsync(new ReleaseDecision(true, "aprobación tardía"));
            });

        // Ganó el rechazo (primera): compensación LIFO igual que la Prueba B.
        Assert.Contains("Final status: Compensated", run.Result);

        var calls = run.Db.StateMachineCalls;
        var refundIdx = calls.IndexOf(nameof(FakeOrderStateMachine.TryMarkPaymentRefundedAsync));
        var cancelIdx = calls.IndexOf(nameof(FakeOrderStateMachine.TryCancelInventoryAsync));
        Assert.True(refundIdx >= 0 && cancelIdx >= 0, "Ambas compensaciones deben ejecutarse.");
        Assert.True(refundIdx < cancelIdx,
            $"El refund debe preceder a la cancelación de inventario (LIFO). calls=[{string.Join(", ", calls)}]");

        Assert.Equal(10, run.Db.Stock[ProductId]);
        Assert.Empty(run.Db.Shipments);
        Assert.Equal("Compensated", run.FinalStatus);
    }

    [Fact] // D.4 — Update aprobado y luego Signal opuesta tardía: la segunda decisión se ignora de punta a punta.
    public async Task UpdateYSignal_PrimeraGana()
    {
        const int orderId = 1006;

        var run = await ReleaseOrderTestEnvironment.RunAsync(
            seed: db => db.SeedOrder(orderId, ProductId, quantity: 2, amount: 100m,
                                     address: "Calle 1", stock: 10),
            orderId: orderId,
            drive: async ctx =>
            {
                await ctx.WaitForStatusAsync("Waiting for release decision");
                await ctx.SubmitDecisionUpdateAsync(new ReleaseDecision(true, "aprobado"));

                // Tras aprobar, el workflow queda parado en el Workflow.DelayAsync(10s) previo
                // al Child Workflow: la Signal tardía siempre llega con el workflow vivo.
                await ctx.SubmitDecisionSignalAsync(new ReleaseDecision(false, "decisión tardía"));
            });

        // Ganó la aprobación (primera): la orden se completa y despacha; la Signal no revirtió nada.
        Assert.Contains("released successfully", run.Result);
        Assert.Equal(new[] { orderId }, run.Db.Shipments);
        Assert.Equal("Completed", run.FinalStatus);

        var calls = run.Db.StateMachineCalls;
        Assert.DoesNotContain(nameof(FakeOrderStateMachine.TryCancelInventoryAsync), calls);
        Assert.DoesNotContain(nameof(FakeOrderStateMachine.TryMarkPaymentRefundedAsync), calls);
    }
}
