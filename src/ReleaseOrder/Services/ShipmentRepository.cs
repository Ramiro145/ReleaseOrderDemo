using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
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