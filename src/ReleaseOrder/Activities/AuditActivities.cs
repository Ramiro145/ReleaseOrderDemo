using System;
using System.Threading.Tasks;
using Temporalio.Activities;

namespace ReleaseOrderDemo.Activities
{
    /// <summary>
    /// Actividad de auditoría del paso agregado por el patch "audit-before-decision"
    /// (ver spec 04 y el bloque Workflow.Patched en ReleaseOrderWorkFlow.cs).
    /// SRP: solo deja rastro de que la orden entró en espera de decisión.
    /// A propósito NO toca SQL ni ningún Service: no interactúa con IOrderStateMachine
    /// ni con la idempotencia del spec 03, así el patch no cambia el efecto de dominio.
    /// </summary>
    public class AuditActivities
    {
        [Activity]
        public Task RecordAwaitingDecisionAsync(int orderId)
        {
            Console.WriteLine($"[Audit] order {orderId} awaiting release decision");
            return Task.CompletedTask;
        }
    }
}
