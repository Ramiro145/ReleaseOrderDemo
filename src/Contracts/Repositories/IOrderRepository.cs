using Contracts.Dtos;

namespace Contracts.Repositories;

public interface IOrderRepository
{
    Task<OrderDto?> GetByIdAsync(string orderId);
    Task AddAsync(OrderDto order);
    Task UpdateStatusAsync(int orderId, string status);
    Task<List<OrderDto>> GetAllAsync();
}
