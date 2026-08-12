using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using Contracts.Dtos;
using Contracts.Repositories;

namespace ReleaseOrderDemo.Services
{
    public class OrderRepository : IOrderRepository
    {
        private readonly string _connectionString;

        public OrderRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<OrderDto?> GetByIdAsync(string orderId)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand("SELECT OrderId, OrderCode, ProductId, Quantity, Amount, Address, Status FROM Orders WHERE OrderId = @OrderId", conn);
            cmd.Parameters.AddWithValue("@OrderId", orderId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new OrderDto
                {
                    OrderId = reader.GetInt32(0),
                    OrderCode = reader.GetString(1),
                    ProductId = reader.GetInt32(2),
                    Quantity = reader.GetInt32(3),
                    Amount = reader.GetDecimal(4),
                    Address = reader.GetString(5),
                    Status = reader.GetString(6)
                };
            }

            return null;
        }

        public async Task AddAsync(OrderDto order)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand(
                "INSERT INTO Orders (OrderId, OrderCode, ProductId, Quantity, Amount, Address, Status) VALUES (@OrderId, @ProductId, @Quantity, @Amount, @Address, @Status)",
                conn);

            cmd.Parameters.AddWithValue("@OrderId", order.OrderId);
            cmd.Parameters.AddWithValue("@OrderCode", order.OrderCode);
            cmd.Parameters.AddWithValue("@ProductId", order.ProductId);
            cmd.Parameters.AddWithValue("@Quantity", order.Quantity);
            cmd.Parameters.AddWithValue("@Amount", order.Amount);
            cmd.Parameters.AddWithValue("@Address", order.Address);
            cmd.Parameters.AddWithValue("@Status", order.Status);

            await cmd.ExecuteNonQueryAsync();
            Console.WriteLine($"[Repository] Order {order.OrderId} inserted into DB");
        }

        public async Task UpdateStatusAsync(int orderId, string status)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand("UPDATE Orders SET Status = @Status WHERE OrderId = @OrderId", conn);
            cmd.Parameters.AddWithValue("@Status", status);
            cmd.Parameters.AddWithValue("@OrderId", orderId);

            var rows = await cmd.ExecuteNonQueryAsync();
            Console.WriteLine(rows > 0
                ? $"[Repository] Order {orderId} status updated to {status}"
                : $"[Repository] Order {orderId} not found");
        }

        public async Task<List<OrderDto>> GetAllAsync()
        {
            var orders = new List<OrderDto>();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand("SELECT OrderId, OrderCode, ProductId, Quantity, Amount, CreatedAt, UpdatedAt, Status FROM Orders", conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                orders.Add(new OrderDto
                {
                    OrderId = reader.GetInt32(0),
                    OrderCode = reader.GetString(1),
                    ProductId = reader.GetInt32(2),
                    Quantity = reader.GetInt32(3),
                    Amount = reader.GetDecimal(4),
                    CreatedAt = DateTime.Parse(reader.GetString(5)),
                    UpdatedAt = DateTime.Parse(reader.GetString(6)),
                    Status = reader.GetString(7)
                });
            }

            return orders;
        }
    }
}
