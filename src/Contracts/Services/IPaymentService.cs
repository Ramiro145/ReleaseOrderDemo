namespace Contracts.Services;

public interface IPaymentService
{
    Task<bool> ProcessAsync(int orderId, decimal amount);
    Task RefundAsync(int orderId);
}
