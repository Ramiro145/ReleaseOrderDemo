using System;
using System.Threading.Tasks;
using Temporalio.Activities;
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
        private readonly IPaymentService _paymentService;
        private readonly IOrderRepository _orderRepository;

        public PaymentActivities(IPaymentService paymentService, IOrderRepository orderRepository)
        {
            _paymentService = paymentService;
            _orderRepository = orderRepository;
        }

        [Activity]
        public async Task<bool> ProcessPaymentAsync(int orderId, decimal amount)
        {
            var success = await _paymentService.ProcessAsync(orderId, amount);
            if (!success)
                throw new ApplicationException($"[Activity] Payment failed for order {orderId}");

            await _orderRepository.UpdateStatusAsync(orderId, "PaymentProcessed");
            Console.WriteLine($"[Activity] Payment processed for order {orderId}");
            return success;
        }

        [Activity]
        public async Task RefundPaymentAsync(int orderId)
        {
            await _paymentService.RefundAsync(orderId);
            await _orderRepository.UpdateStatusAsync(orderId, "PaymentRefunded");
            Console.WriteLine($"[Activity] Payment refunded for order {orderId}");
        }
    }
}
