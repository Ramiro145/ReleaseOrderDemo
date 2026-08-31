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
    /// DIP: depende de IPaymentService e IOrderRepository (abstracciones).
    /// </summary>
    public class PaymentActivities
    {
        // Monto mágico para el demo: simula un timeout transitorio del gateway
        // (reintentable) en vez de un rechazo de negocio, para contrastar con el
        // caso no-reintentable (amount <= 0, ver PaymentService.ProcessAsync).
        public const decimal TransientFailureAmount = 999999m;

        private readonly IPaymentService _paymentService;
        private readonly IOrderRepository _orderRepository;
        private readonly IIdempotencyLedger _ledger;

        public PaymentActivities(
            IPaymentService paymentService,
            IOrderRepository orderRepository,
            IIdempotencyLedger ledger)
        {
            _paymentService = paymentService;
            _orderRepository = orderRepository;
            _ledger = ledger;
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

            return await IdempotentActivity.RunAsync(_ledger, orderId, async () =>
            {
                var success = await _paymentService.ProcessAsync(orderId, amount);
                if (!success)
                    // Rechazo de negocio (ej: gateway declina la tarjeta): reintentar no cambia
                    // el resultado, así que se marca como no-reintentable para que el SAGA pase
                    // directo a compensación en vez de esperar los reintentos de DefaultOptions.
                    throw new ApplicationFailureException(
                        $"[Activity] Payment declined for order {orderId}",
                        errorType: "PaymentDeclined",
                        nonRetryable: true);

                await _orderRepository.UpdateStatusAsync(orderId, "PaymentProcessed");
                Console.WriteLine($"[Activity] Payment processed for order {orderId}");
                return success;
            });
        }

        [Activity]
        public async Task RefundPaymentAsync(int orderId)
        {
            await IdempotentActivity.RunAsync(_ledger, orderId, async () =>
            {
                await _paymentService.RefundAsync(orderId);
                await _orderRepository.UpdateStatusAsync(orderId, "PaymentRefunded");
                Console.WriteLine($"[Activity] Payment refunded for order {orderId}");
            });
        }
    }
}
