using Temporalio.Workflows;
using Temporalio.Common; // <-- necesario para RetryPolicy
using OrderReportDemo.Activities;
using Contracts.Workflows;
using Contracts.Dtos;

namespace OrderReportDemo.Workflows;

[Workflow]
public class OrderReportWorkflow : IOrderReportWorkflow
{
    [WorkflowRun]
    public async Task<OrderReportResult> RunAsync(int orderId)
    {
        // Ejecuta la activity para construir el reporte
        var report = await Workflow.ExecuteActivityAsync(
            (OrderReportActivities a) => a.GenerateOrderReportAsync(orderId),
            new ActivityOptions
            {
                StartToCloseTimeout = TimeSpan.FromSeconds(15),
                RetryPolicy = new RetryPolicy
                {
                    MaximumAttempts = 3
                }
            });

        // Arma un resultado amigable
        var summary = $"Order {report.OrderId} → Status: {report.Status}, WorkflowResult: {report.WorkflowResult}";
        return new OrderReportResult{
            OrderId= report.OrderId,
            Status= report.Status,
            GeneratedAt= Workflow.UtcNow,
            Summary= summary
        };
    }
}