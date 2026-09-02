using Contracts.Dtos;
using Contracts.Repositories;

namespace ReleaseOrder.Tests.Fakes;

/// <summary>
/// Fake de <see cref="IProductRepository"/>. Solo lo usa
/// <c>InventoryService.CheckAvailabilityAsync</c>; el decremento real de stock lo
/// hace <see cref="FakeOrderStateMachine"/>, así que este fake refleja el stock vivo.
/// </summary>
public sealed class FakeProductRepository : IProductRepository
{
    private readonly FakeOrderDatabase _db;

    public FakeProductRepository(FakeOrderDatabase db) => _db = db;

    public Task<ProductDto?> GetByIdAsync(int productId)
    {
        if (!_db.Stock.TryGetValue(productId, out var stock))
            return Task.FromResult<ProductDto?>(null);

        return Task.FromResult<ProductDto?>(new ProductDto
        {
            ProductId = productId,
            Code = $"P-{productId}",
            Name = $"Product {productId}",
            Description = "test",
            Stock = stock,
            Price = 1m,
            IsActive = true,
        });
    }
}
