using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Contracts.Services;

namespace ReleaseOrderDemo.Services
{
    /// <summary>
    /// Servicio que simula el procesamiento de pagos.
    /// En un escenario real, aquí se conectaría a un gateway de pagos (Stripe, PayPal, etc.).
    /// FailPayment es un flag de prueba que NO forma parte de la interfaz pública.
    /// </summary>
    public class PaymentService : IPaymentService
    {
        // Simulación de pagos procesados en memoria
        private readonly HashSet<int> _processedPayments = new();

        // Flag para simular fallo en pruebas
        public bool FailPayment { get; set; } = false;

        /// <summary>
        /// Procesa el pago de una orden.
        /// </summary>
        public Task<bool> ProcessAsync(int orderId, decimal amount)
        {
            if (FailPayment)
            {
                Console.WriteLine($"[PaymentService] Payment FAILED for order {orderId}");
                return Task.FromResult(false);
            }

            _processedPayments.Add(orderId);
            Console.WriteLine($"[PaymentService] Payment processed for order {orderId}");
            return Task.FromResult(true);
        }

        /// <summary>
        /// Reembolsa un pago previamente procesado (compensación).
        /// </summary>
        public Task RefundAsync(int orderId)
        {
            if (_processedPayments.Contains(orderId))
            {
                _processedPayments.Remove(orderId);
                Console.WriteLine($"[PaymentService] Payment refunded for order {orderId}");
            }
            else
            {
                Console.WriteLine($"[PaymentService] No payment found to refund for {orderId}");
            }

            return Task.CompletedTask;
        }
    }
}