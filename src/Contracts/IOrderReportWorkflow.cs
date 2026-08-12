using Temporalio.Workflows;
using Contracts.Dtos;

namespace Contracts.Workflows;

[Workflow]
public interface IOrderReportWorkflow
{
    [WorkflowRun]
    Task<OrderReportResult> RunAsync(int orderId);
}