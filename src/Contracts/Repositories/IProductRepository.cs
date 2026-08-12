using Contracts.Dtos;

namespace Contracts.Repositories;

public interface IProductRepository
{
    Task<ProductDto?> GetByIdAsync(int productId);
    Task UpdateStockAsync(int productId, int newStock);
}
