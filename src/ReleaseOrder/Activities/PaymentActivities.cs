using System;
using System.Threading.Tasks;
using Temporalio.Activities;
using Temporalio.Exceptions;
using Contracts.Services;
using Contracts.Repositories;

namespace ReleaseOrderDemo.Activities
{
    /// <summary>
    /// Actividades de dominio de pagos.
    /// SRP: solo gestiona procesamiento y reembolso de pagos.
    /// DIP: depende de IPaymentService e IOrderStateMachine (abstracciones). La
    /// idempotencia frente al at-least-once de Temporal la da IOrderStateMachine
    /// (Orders.Status), que avanza en la misma transacción que valida el reintento.
    /// </summary>
    public class PaymentActivities
    {
        // Monto mágico para el demo: simula un timeout transitorio del gateway
        // (reintentable) en vez de un rechazo de negocio, para contrastar con el
        // caso no-reintentable (amount <= 0, ver PaymentService.ProcessAsync).
        public const decimal TransientFailureAmount = 999999m;

        // Monto mágico para el demo de idempotencia: en el primer intento lanza
        // DESPUÉS de que Orders.Status ya avanzó a 'PaymentProcessed', pero antes de
        // que la actividad reporte completitud. Así el reintento (attempt 2) encuentra
        // el Status ya avanzado y no duplica el efecto.
        public const decimal ReplayProbeAmount = 888888m;

        private readonly IPaymentService _paymentService;
        private readonly IOrderStateMachine _stateMachine;

        public PaymentActivities(IPaymentService paymentService, IOrderStateMachine stateMachine)
        {
            _paymentService = paymentService;
            _stateMachine = stateMachine;
        }

        [Activity]
        public async Task<bool> ProcessPaymentAsync(int orderId, decimal amount)
        {
            if (amount == TransientFailureAmount)
            {
                var attempt = ActivityExecutionContext.Current.Info.Attempt;
                Console.WriteLine(
                    $"[Activity] Simulated transient gateway timeout for order {orderId} (attempt {attempt})");
                // Excepción genérica: Temporal la trata como reintentable y aplica
                // el RetryPolicy de DefaultOptions (3 intentos con backoff) antes
                // de que el SAGA compense.
                throw new ApplicationException(
                    $"[Activity] Payment gateway timeout for order {orderId} (attempt {attempt})");
            }

            // PaymentService.ProcessAsync es naturalmente idempotente (Add/Contains sobre un
            // HashSet), así que se llama en cada intento: la decisión de negocio (aprobar/
            // declinar) no necesita guardarse aparte. Lo que sí debe ser idempotente frente
            // al at-least-once de Temporal es el avance de Orders.Status, que hace
            // TryMarkPaymentProcessedAsync en una transacción atómica.
            var success = await _paymentService.ProcessAsync(orderId, amount);
            if (!success)
                // Rechazo de negocio (ej: gateway declina la tarjeta): reintentar no cambia
                // el resultado, así que se marca como no-reintentable para que el SAGA pase
                // directo a compensación en vez de esperar los reintentos de DefaultOptions.
                throw new ApplicationFailureException(
                    $"[Activity] Payment declined for order {orderId}",
                    errorType: "PaymentDeclined",
                    nonRetryable: true);

            var outcome = await _stateMachine.TryMarkPaymentProcessedAsync(orderId);
            if (outcome is StepOutcome.OrderNotFound)
                throw new ApplicationFailureException(
                    $"[Activity] Order {orderId} not found", errorType: "OrderNotFound", nonRetryable: true);

            Console.WriteLine($"[Activity] Payment processed for order {orderId}");

            // Replay probe: Orders.Status ya avanzó a 'PaymentProcessed' arriba; lanzamos
            // ahora (reintentable) para que Temporal reintente y el attempt 2 caiga en la
            // guarda de AlreadyApplied sin volver a aplicar el pago.
            if (amount == ReplayProbeAmount && ActivityExecutionContext.Current.Info.Attempt == 1)
            {
                Console.WriteLine(
                    $"[Activity] Replay probe for order {orderId}: throwing after status advance on attempt 1");
                throw new ApplicationException(
                    $"[Activity] Replay probe forced retry for order {orderId} (attempt 1)");
            }

            return success;
        }

        [Activity]
        public async Task RefundPaymentAsync(int orderId)
        {
            var outcome = await _stateMachine.TryMarkPaymentRefundedAsync(orderId);
            if (outcome is StepOutcome.AlreadyApplied)
            {
                Console.WriteLine($"[Activity] Payment already refunded for order {orderId} (idempotent retry)");
                return;
            }

            await _paymentService.RefundAsync(orderId);
            Console.WriteLine($"[Activity] Payment refunded for order {orderId}");
        }
    }
}
