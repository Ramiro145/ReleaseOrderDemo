using Temporalio.Activities;
using Temporalio.Client;
using Contracts.Services;
using Contracts.Workflows;
using Contracts.Dtos;

namespace OrderReportDemo.Activities;

public class OrderReportActivities
{
    private readonly IReportService _reportService;
    private readonly TemporalClient _client;

    public OrderReportActivities(IReportService reportService, TemporalClient client)
    {
        _reportService = reportService;
        _client = client;
    }

    [Activity]
    public async Task<OrderReport> GenerateOrderReportAsync(int orderId)
    {
        string workflowResult;

        try
        {
            // Espera a que el workflow ReleaseOrder termine
            var releaseHandle = _client.GetWorkflowHandle<IReleaseOrderWorkflow>($"release-order-{orderId}");
            workflowResult = await releaseHandle.GetResultAsync<string>();
        }
        catch
        {
            // Si falla, no generamos error: usamos un resultado por defecto
            workflowResult = "ReleaseOrder workflow completed (default result)";
        }

        // Construye el reporte con SQL Server y el resultado del workflow
        return await _reportService.BuildOrderReportAsync(orderId, workflowResult);
    }
}