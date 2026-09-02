using Google.Protobuf;
using Temporalio.Api.Enums.V1;
using Temporalio.Api.History.V1;
using Temporalio.Api.WorkflowService.V1;
using Temporalio.Client;
using WorkflowExecution = Temporalio.Api.Common.V1.WorkflowExecution;

namespace ReleaseOrder.Tests.Support;

/// <summary>
/// Aserciones sobre el Event History de un workflow ya terminado. Es la única forma de
/// contar intentos de una Activity sin tocar producción: con <c>Amount = 999999</c>,
/// <c>PaymentActivities</c> lanza <b>antes</b> de llamar a <c>PaymentService</c>, así que
/// un contador en los fakes nunca se incrementaría.
///
/// <para>
/// Temporalio 1.9.0 no expone la historia desde el <c>WorkflowHandle</c> (ese método se
/// agregó en versiones posteriores), así que se usa la llamada gRPC cruda
/// <c>WorkflowService.GetWorkflowExecutionHistoryAsync</c>.
/// </para>
/// </summary>
public sealed class HistoryAssertions
{
    private readonly IReadOnlyList<HistoryEvent> _events;

    private HistoryAssertions(IReadOnlyList<HistoryEvent> events) => _events = events;

    public static async Task<HistoryAssertions> FetchAsync(
        ITemporalClient client, string workflowId, string? runId)
    {
        var events = new List<HistoryEvent>();
        var pageToken = ByteString.Empty;

        // A esta escala una sola página alcanza; el bucle es barato y evita sorpresas
        // en las tandas siguientes (idempotencia, con historias más largas).
        do
        {
            var response = await client.WorkflowService.GetWorkflowExecutionHistoryAsync(
                new GetWorkflowExecutionHistoryRequest
                {
                    Namespace = client.Options.Namespace,
                    Execution = new WorkflowExecution
                    {
                        WorkflowId = workflowId,
                        RunId = runId ?? string.Empty,
                    },
                    NextPageToken = pageToken,
                });

            events.AddRange(response.History.Events);
            pageToken = response.NextPageToken;
        }
        while (!pageToken.IsEmpty);

        return new HistoryAssertions(events);
    }

    /// <summary>
    /// Intentos de la Activity <paramref name="activityName"/> (el sufijo <c>Async</c> es
    /// opcional: en la historia el tipo va sin él). Toma el máximo entre (a) la cantidad
    /// de <c>ActivityTaskStarted</c> y (b) el mayor <c>Attempt</c> visto: el test server
    /// puede codificar los reintentos de cualquiera de las dos formas — un evento
    /// <c>Started</c> por intento, o uno solo con <c>Attempt = N</c> (los intermedios son
    /// "transient" y no quedan en la historia final).
    /// </summary>
    public int AttemptsFor(string activityName)
    {
        var scheduledIds = ScheduledEventIdsFor(activityName);

        var started = _events
            .Select(e => e.ActivityTaskStartedEventAttributes)
            .Where(a => a is not null && scheduledIds.Contains(a.ScheduledEventId))
            .ToList();

        var byCount = started.Count;
        var byAttempt = started.Count == 0 ? 0 : started.Max(a => a.Attempt);
        return Math.Max(byCount, byAttempt);
    }

    /// <summary>
    /// <c>errorType</c> del último <c>ActivityTaskFailed</c> de
    /// <paramref name="activityName"/> — el <c>Type</c> de su <c>ApplicationFailureInfo</c>.
    /// Sirve para asertar <c>"PaymentDeclined"</c> e <c>"InventoryUnavailableException"</c>.
    /// </summary>
    public string? FailureErrorTypeFor(string activityName)
    {
        var scheduledIds = ScheduledEventIdsFor(activityName);

        return _events
            .Select(e => e.ActivityTaskFailedEventAttributes)
            .Where(a => a is not null && scheduledIds.Contains(a.ScheduledEventId))
            .Select(a => a.Failure?.ApplicationFailureInfo?.Type)
            .LastOrDefault(type => !string.IsNullOrEmpty(type));
    }

    /// <summary>Cantidad de eventos de <paramref name="type"/> en la historia.</summary>
    public int CountEventType(EventType type) => _events.Count(e => e.EventType == type);

    /// <summary>
    /// <c>true</c> si la historia contiene al menos un evento de <paramref name="type"/>.
    /// Para asertar desde la historia del <b>padre</b> que hubo
    /// <c>StartChildWorkflowExecutionInitiated</c> y que el hijo cerró en
    /// <c>ChildWorkflowExecutionFailed</c> vs <c>ChildWorkflowExecutionCompleted</c>.
    /// </summary>
    public bool ContainsEventType(EventType type) => CountEventType(type) > 0;

    /// <summary>
    /// EventIds de los <c>ActivityTaskScheduled</c> de la actividad pedida. Los eventos
    /// <c>Started</c>/<c>Failed</c> no llevan el nombre, solo un <c>ScheduledEventId</c>
    /// que apunta acá.
    /// </summary>
    private HashSet<long> ScheduledEventIdsFor(string activityName) =>
        _events
            .Where(e => e.ActivityTaskScheduledEventAttributes is not null &&
                        Matches(e.ActivityTaskScheduledEventAttributes.ActivityType.Name, activityName))
            .Select(e => e.EventId)
            .ToHashSet();

    private static bool Matches(string historyName, string queried) =>
        historyName == queried ||
        historyName == queried + "Async" ||
        historyName + "Async" == queried;
}
