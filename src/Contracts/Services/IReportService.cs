using Contracts.Dtos;

namespace Contracts.Services;

public interface IReportService
{
    Task<OrderReport> BuildOrderReportAsync(int orderId, string workflowResult, CancellationToken ct = default);
}
