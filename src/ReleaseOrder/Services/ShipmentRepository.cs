using System;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using Contracts.Dtos;
using Contracts.Repositories;

namespace ReleaseOrderDemo.Services
{
    public class ShipmentRepository : IShipmentRepository
    {
        private readonly string _connectionString;

        public ShipmentRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task InsertAsync(ShipmentDto shipment)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand(
                "INSERT INTO Shipments (OrderId, Address, Status, CreatedAt) VALUES (@OrderId, @Address, @Status, @CreatedAt)",
                conn
            );

            cmd.Parameters.AddWithValue("@OrderId", shipment.OrderId);
            cmd.Parameters.AddWithValue("@Address", shipment.Address);
            cmd.Parameters.AddWithValue("@Status", shipment.Status);
            cmd.Parameters.AddWithValue("@CreatedAt", shipment.CreatedAt);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateStatusAsync(int shipmentId, string status)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand("UPDATE Shipments SET Status = @Status WHERE ShipmentId = @ShipmentId", conn);
            cmd.Parameters.AddWithValue("@Status", status);
            cmd.Parameters.AddWithValue("@ShipmentId", shipmentId);

            await cmd.ExecuteNonQueryAsync();
        }
    }
}