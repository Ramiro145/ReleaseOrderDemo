using Temporalio.Workflows;

namespace Contracts.Workflows;

/// <summary>
/// Child Workflow que gestiona el envío de una orden ya liberada.
/// Se ejecuta como Workflow Execution independiente (su propio Workflow Id
/// y Event History), iniciado por ReleaseOrderWorkflow como su parent.
/// </summary>
[Workflow]
public interface IShippingWorkflow
{
    [WorkflowRun]
    Task<string> RunAsync(int orderId, string address);
}
