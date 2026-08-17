using System;

namespace ReleaseOrderDemo.Activities
{
    /// <summary>
    /// Error de negocio: no hay stock. Es una excepción .NET común y corriente —
    /// no usa la API de Temporal (a diferencia de PaymentActivities). La marca de
    /// "no reintentable" se declara del lado del workflow, vía
    /// RetryPolicy.NonRetryableErrorTypes en ReleaseOrderWorkFlow.cs.
    /// </summary>
    public class InventoryUnavailableException : ApplicationException
    {
        public InventoryUnavailableException(string message) : base(message)
        {
        }
    }
}
