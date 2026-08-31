using System;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using Contracts.Repositories;

namespace ReleaseOrderDemo.Services
{
    /// <summary>
    /// Ledger de idempotencia sobre la tabla dbo.ProcessedActivities.
    /// Mismo patrón que los repos existentes: connection string por constructor,
    /// SqlConnection nueva por llamada.
    /// </summary>
    public class IdempotencyLedger : IIdempotencyLedger
    {
        // Violación de clave única / PK en SQL Server.
        private const int SqlErrorUniqueConstraint = 2627;
        private const int SqlErrorDuplicateKey = 2601;

        private readonly string _connectionString;

        public IdempotencyLedger(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<LedgerEntry?> TryGetAsync(string key)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand(
                "SELECT IdempotencyKey, ResultJson FROM ProcessedActivities WHERE IdempotencyKey = @Key",
                conn);
            cmd.Parameters.AddWithValue("@Key", key);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var resultJson = reader.IsDBNull(1) ? null : reader.GetString(1);
                return new LedgerEntry(reader.GetString(0), resultJson);
            }

            return null;
        }

        public async Task<bool> SaveAsync(
            string key, string workflowId, string activityType, int orderId, string? resultJson)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand(
                "INSERT INTO ProcessedActivities (IdempotencyKey, WorkflowId, ActivityType, OrderId, ResultJson) " +
                "VALUES (@Key, @WorkflowId, @ActivityType, @OrderId, @ResultJson)",
                conn);
            cmd.Parameters.AddWithValue("@Key", key);
            cmd.Parameters.AddWithValue("@WorkflowId", workflowId);
            cmd.Parameters.AddWithValue("@ActivityType", activityType);
            cmd.Parameters.AddWithValue("@OrderId", orderId);
            cmd.Parameters.AddWithValue("@ResultJson", (object?)resultJson ?? DBNull.Value);

            try
            {
                await cmd.ExecuteNonQueryAsync();
                Console.WriteLine($"[Ledger] saved {key}");
                return true;
            }
            catch (SqlException ex) when (
                ex.Number == SqlErrorUniqueConstraint || ex.Number == SqlErrorDuplicateKey)
            {
                // Otro intento concurrente ganó la carrera; el llamador re-lee con TryGetAsync.
                Console.WriteLine($"[Ledger] collision on {key}, another attempt won");
                return false;
            }
        }
    }
}
