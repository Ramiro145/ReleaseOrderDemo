using Contracts.Workflows;
using ReleaseOrderDemo.Activities;
using Temporalio.Common;
using Temporalio.Workflows;

namespace ReleaseOrderDemo.Workflows;

[Workflow]
public class ShippingWorkflow : IShippingWorkflow
{
    private static readonly ActivityOptions DefaultOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromSeconds(30),
        RetryPolicy = new RetryPolicy
        {
            MaximumAttempts = 3,
            InitialInterval = TimeSpan.FromSeconds(1),
            BackoffCoefficient = 2.0F
        }
    };

    [WorkflowRun]
    public async Task<string> RunAsync(int orderId, string address)
    {
        await Workflow.ExecuteActivityAsync(
            (ShippingActivities a) => a.ShipOrderAsync(orderId, address),
            DefaultOptions);

        return $"Order {orderId} shipped to {address}";
    }
}
