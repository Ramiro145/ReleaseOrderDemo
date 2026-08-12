using System.Data;
using Microsoft.Data.SqlClient;
using Contracts.Dtos;
using Contracts.Services;

namespace OrderReportDemo.Services;

public class ReportService : IReportService
{
    private readonly string _connectionString;

    public ReportService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<OrderReport> BuildOrderReportAsync(int orderId, string workflowResult, CancellationToken ct = default)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(@"
            SELECT OrderId, Status, CreatedAt, UpdatedAt
            FROM Orders
            WHERE OrderId = @OrderId", conn);

        cmd.Parameters.Add(new SqlParameter("@OrderId", SqlDbType.NVarChar, 50) { Value = orderId });

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return new OrderReport{
                OrderId= reader.GetInt32(0),
                Status= reader.GetString(1),
                CreatedAt= reader.GetDateTime(2),
                UpdatedAt= reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                WorkflowResult= workflowResult
            };
        }

        return new OrderReport{
            OrderId= orderId,
            Status= "NotFound",
            CreatedAt= DateTime.UtcNow,
            UpdatedAt= null,
            WorkflowResult= workflowResult
        };
    }
}