using Contracts.Workflows;
using ReleaseOrder.Tests.Fakes;
using ReleaseOrderDemo.Activities;
using ReleaseOrderDemo.Services;
using ReleaseOrderDemo.Workflows;
using Temporalio.Client;
using Temporalio.Testing;
using Temporalio.Worker;
using Xunit;

namespace ReleaseOrder.Tests.Support;

/// <summary>
/// Monta las tres piezas del testing de Temporal para <c>ReleaseOrderWorkflow</c>:
///   1. <see cref="WorkflowEnvironment.StartTimeSkippingAsync"/> — servidor embebido con reloj acelerado.
///   2. <see cref="TemporalWorker"/> con el workflow, su Child Workflow y las Activities reales
///      (Services reales; solo el borde SQL va con fakes en memoria).
///   3. <c>worker.ExecuteAsync(...)</c> arranca el workflow, corre el <paramref name="drive"/>
///      (que manda las señales) y espera el resultado.
/// </summary>
public static class ReleaseOrderTestEnvironment
{
    public static async Task<ReleaseOrderRunResult> RunAsync(
        Action<FakeOrderDatabase> seed,
        int orderId,
        Func<ReleaseOrderDriveContext, Task> drive)
    {
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();

        var db = new FakeOrderDatabase();
        seed(db);

        // Services de producción reales; solo IOrderStateMachine + repos son fakes.
        var stateMachine = new FakeOrderStateMachine(db);
        var inventory = new InventoryService(new FakeProductRepository(db), stateMachine);
        var payment = new PaymentService();
        var shipping = new ShippingService(
            new FakeShipmentRepository(db), new FakeOrderRepository(db), stateMachine);

        // Task queue única por corrida: el Child Workflow usa un Id fijo
        // ($"shipping-order-{orderId}"), así que aislar por queue + orderId evita choques.
        var taskQueue = $"release-order-test-{Guid.NewGuid():N}";

        using var worker = new TemporalWorker(
            env.Client,
            new TemporalWorkerOptions(taskQueue)
                .AddAllActivities(new InventoryActivities(inventory))
                .AddAllActivities(new PaymentActivities(payment, stateMachine))
                .AddAllActivities(new ShippingActivities(shipping))
                .AddAllActivities(new OrderStatusActivities(new FakeOrderRepository(db)))
                .AddAllActivities(new OrderLookupActivities(new FakeOrderRepository(db)))
                .AddWorkflow<ReleaseOrderWorkflow>()
                .AddWorkflow<ShippingWorkflow>());

        return await worker.ExecuteAsync(async () =>
        {
            var handle = await env.Client.StartWorkflowAsync(
                (ReleaseOrderWorkflow wf) => wf.RunAsync(orderId),
                new WorkflowOptions($"release-order-{orderId}", taskQueue));

            var ctx = new ReleaseOrderDriveContext(env, handle);
            await drive(ctx);

            var result = await handle.GetResultAsync();
            var finalStatus = await handle.QueryAsync(wf => wf.GetStatus());

            return new ReleaseOrderRunResult(result, db, finalStatus);
        });
    }
}

/// <summary>Resultado de una corrida: string devuelto por el workflow, estado de la BD fake y última Query.</summary>
public sealed record ReleaseOrderRunResult(string Result, FakeOrderDatabase Db, string FinalStatus);

/// <summary>Handle + entorno, con el helper de espera sobre la Query <c>GetStatus</c>.</summary>
public sealed class ReleaseOrderDriveContext
{
    private readonly WorkflowEnvironment _env;

    public ReleaseOrderDriveContext(
        WorkflowEnvironment env, WorkflowHandle<ReleaseOrderWorkflow, string> handle)
    {
        _env = env;
        Handle = handle;
    }

    public WorkflowHandle<ReleaseOrderWorkflow, string> Handle { get; }

    /// <summary>
    /// Consulta la Query <c>GetStatus</c> hasta ver <paramref name="target"/>, empujando el reloj
    /// con <c>env.DelayAsync</c> entre intentos (el auto time-skipping se suspende mientras hay una
    /// llamada del cliente en vuelo, así que hay que avanzar el tiempo a mano para pasar los
    /// <c>Workflow.DelayAsync</c> de 5s y 10s del workflow).
    /// </summary>
    public async Task WaitForStatusAsync(string target, int maxSteps = 60)
    {
        var last = "";
        for (var i = 0; i < maxSteps; i++)
        {
            last = await Handle.QueryAsync(wf => wf.GetStatus());
            if (last == target)
                return;
            await _env.DelayAsync(TimeSpan.FromSeconds(1));
        }

        Assert.Fail($"El workflow nunca llegó a '{target}' (último estado visto: '{last}').");
    }
}
