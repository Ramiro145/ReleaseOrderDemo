using Temporalio.Workflows;

namespace Contracts.Workflows;

/// <summary>
/// Workflow que gestiona la liberación de una orden.
/// </summary>
[Workflow]
public interface IReleaseOrderWorkflow
{
    // Inicia la SAGA y espera una decisión externa después del pago.
    [WorkflowRun]
    Task<string> RunAsync(int orderId);

    // Signal: entrega una decisión externa, pero no devuelve un resultado.
    [WorkflowSignal]
    Task SubmitReleaseDecisionAsync(ReleaseDecision decision);

    // Query opcional para consultar el estado actual
    [WorkflowQuery]
    string GetStatus();
}

/// <summary>
/// Mensaje enviado al Workflow mediante Signal.
/// </summary>
public record ReleaseDecision(bool Approved, string? Reason = null);
