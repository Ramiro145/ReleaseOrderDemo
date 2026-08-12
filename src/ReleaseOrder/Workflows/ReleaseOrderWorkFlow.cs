using Contracts.Workflows;
using ReleaseOrderDemo.Activities;
using Temporalio.Common;
using Temporalio.Workflows;

namespace ReleaseOrderDemo.Workflows;

[Workflow]
public class ReleaseOrderWorkflow : IReleaseOrderWorkflow
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

    private static readonly ActivityOptions CompensationOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromSeconds(30),
        RetryPolicy = new RetryPolicy
        {
            MaximumAttempts = 5,
            InitialInterval = TimeSpan.FromSeconds(1),
            BackoffCoefficient = 2.0F
        }
    };

    private string _status = "Starting";
    private bool _decisionReceived;
    private ReleaseDecision? _decision;

    [WorkflowRun]
    public async Task<string> RunAsync(int orderId)
    {
        var compensations = new Stack<Func<Task>>();

        try
        {
            _status = "Loading order";
            var order = await Workflow.ExecuteActivityAsync(
                (OrderLookupActivities a) => a.GetOrderAsync(orderId),
                DefaultOptions);

            _status = "Reserving inventory";
            await Workflow.ExecuteActivityAsync(
                (InventoryActivities a) => a.ReserveInventoryAsync(
                    orderId,
                    order.ProductId,
                    order.Quantity),
                DefaultOptions);

            compensations.Push(() => Workflow.ExecuteActivityAsync(
                (InventoryActivities a) => a.CancelInventoryAsync(
                    orderId,
                    order.ProductId,
                    order.Quantity),
                CompensationOptions));

            _status = "Processing payment";
            await Workflow.ExecuteActivityAsync(
                (PaymentActivities a) => a.ProcessPaymentAsync(
                    orderId,
                    order.Amount),
                DefaultOptions);

            compensations.Push(() => Workflow.ExecuteActivityAsync(
                (PaymentActivities a) => a.RefundPaymentAsync(orderId),
                CompensationOptions));

            // El Workflow queda durablemente abierto hasta recibir la Signal.
            // No realiza polling y no mantiene ocupado un hilo del Worker.
            _status = "Waiting for release decision";
            await Workflow.WaitConditionAsync(() => _decisionReceived);

            if (!_decision!.Approved)
                throw new ApplicationException(
                    $"Release rejected: {_decision.Reason ?? "no reason provided"}");

            _status = "Completing order";
            await Workflow.ExecuteActivityAsync(
                (OrderStatusActivities a) => a.UpdateOrderStatusAsync(
                    orderId,
                    "Completed"),
                DefaultOptions);

            _status = "Completed";
            return $"Order {orderId} released successfully at {Workflow.UtcNow}";
        }
        catch (Exception failure)
        {
            _status = "Compensating";
            var compensationFailures = 0;
            var completedSteps = compensations.Count;

            while (compensations.TryPop(out var compensate))
            {
                try
                {
                    await compensate();
                }
                catch
                {
                    compensationFailures++;
                }
            }

            var finalStatus = compensationFailures > 0
                ? "CompensationFailed"
                : completedSteps > 0
                    ? "Compensated"
                    : "Failed";

            try
            {
                await Workflow.ExecuteActivityAsync(
                    (OrderStatusActivities a) => a.UpdateOrderStatusAsync(
                        orderId,
                        finalStatus),
                    CompensationOptions);
            }
            catch
            {
                finalStatus = "CompensationFailed";
            }

            _status = finalStatus;
            return $"Order {orderId} failed ({failure.Message}). Final status: {finalStatus}";
        }
    }

    [WorkflowSignal]
    public Task SubmitReleaseDecisionAsync(ReleaseDecision decision)
    {
        // Para esta demo, la primera decisión gana. Esto también vuelve
        // idempotente el manejo de Signals duplicadas.
        if (!_decisionReceived)
        {
            _decision = decision;
            _decisionReceived = true;
        }

        return Task.CompletedTask;
    }

    [WorkflowQuery]
    public string GetStatus() => _status;
}
