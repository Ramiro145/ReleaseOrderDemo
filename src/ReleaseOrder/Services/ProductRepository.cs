using System;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using Contracts.Dtos;
using Contracts.Repositories;

namespace ReleaseOrderDemo.Services
{
    public class ProductRepository : IProductRepository
    {
        private readonly string _connectionString;

        public ProductRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<ProductDto?> GetByIdAsync(int productId)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand(
                """
                SELECT ProductId, Code, Name, Stock, Price, IsActive
                FROM Products
                WHERE ProductId = @ProductId
                """,
                conn);
            cmd.Parameters.AddWithValue("@ProductId", productId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            return new ProductDto
            {
                ProductId = reader.GetInt32(reader.GetOrdinal("ProductId")),
                Code = reader.GetString(reader.GetOrdinal("Code")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Description = string.Empty,
                Stock = reader.GetInt32(reader.GetOrdinal("Stock")),
                Price = reader.GetDecimal(reader.GetOrdinal("Price")),
                IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive"))
            };
        }

        public async Task UpdateStockAsync(int productId, int newStock)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand("UPDATE Products SET Stock = @Stock WHERE ProductId = @ProductId", conn);
            cmd.Parameters.AddWithValue("@Stock", newStock);
            cmd.Parameters.AddWithValue("@ProductId", productId);

            await cmd.ExecuteNonQueryAsync();
        }
    }
}
