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

    // Mismo timeout/retries que DefaultOptions, pero declara del lado del workflow que
    // InventoryUnavailableException (falta de stock, error de negocio) no debe reintentarse.
    // Contrasta con PaymentActivities, donde la Activity misma decide con
    // ApplicationFailureException(nonRetryable: true).
    private static readonly ActivityOptions InventoryReserveOptions = new()
    {
        StartToCloseTimeout = TimeSpan.FromSeconds(30),
        RetryPolicy = new RetryPolicy
        {
            MaximumAttempts = 3,
            InitialInterval = TimeSpan.FromSeconds(1),
            BackoffCoefficient = 2.0F,
            NonRetryableErrorTypes = new[] { nameof(InventoryUnavailableException) }
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
            // TEMPORAL: delay de prueba para poder probar el rechazo del
            // validador del Update mientras el estado no es "Waiting for
            // release decision". Revertir luego de probar.
            await Workflow.DelayAsync(TimeSpan.FromSeconds(5));
            var order = await Workflow.ExecuteActivityAsync(
                (OrderLookupActivities a) => a.GetOrderAsync(orderId),
                DefaultOptions);

            _status = "Reserving inventory";
            await Workflow.ExecuteActivityAsync(
                (InventoryActivities a) => a.ReserveInventoryAsync(
                    orderId,
                    order.ProductId,
                    order.Quantity),
                InventoryReserveOptions);

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

            // Child Workflow: Workflow Execution independiente (su propio Workflow Id
            // y Event History), anidado bajo este parent en Temporal UI. Si agota sus
            // reintentos y falla, la excepción propaga hacia este try y dispara la
            // misma compensación LIFO que un fallo de Activity — el SAGA no distingue
            // entre ambos.
            _status = "Shipping order";
            await Workflow.DelayAsync(TimeSpan.FromSeconds(10));
            var shippingResult = await Workflow.ExecuteChildWorkflowAsync(
                (ShippingWorkflow wf) => wf.RunAsync(orderId, order.Address),
                new ChildWorkflowOptions
                {
                    Id = $"shipping-order-{orderId}"
                });

            _status = "Completed";
            return $"Order {orderId} released successfully at {Workflow.UtcNow}. {shippingResult}";
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

    [WorkflowUpdate]
    public Task<string> SubmitReleaseDecisionUpdateAsync(ReleaseDecision decision)
    {
        // A diferencia de la Signal, el validador ya descartó estados inválidos
        // antes de llegar aquí, así que solo queda aplicar la misma regla de
        // "la primera decisión gana" para tolerar reintentos del cliente.
        if (!_decisionReceived)
        {
            _decision = decision;
            _decisionReceived = true;
        }

        return Task.FromResult(decision.Approved
            ? "Decision accepted: order will be completed."
            : "Decision accepted: order will be compensated.");
    }

    [WorkflowUpdateValidator(nameof(SubmitReleaseDecisionUpdateAsync))]
    public void ValidateSubmitReleaseDecisionUpdate(ReleaseDecision decision)
    {
        // El validador corre síncronamente en el Worker antes de que el Update
        // se acepte: si falla, no queda evento en el Event History y el
        // llamador recibe el rechazo de inmediato (la Signal no puede hacer esto).
        if (_status != "Waiting for release decision")
            throw new ApplicationException(
                $"Cannot submit release decision via Update: workflow is in '{_status}' state, not waiting for a decision.");
    }

    [WorkflowQuery]
    public string GetStatus() => _status;
}
