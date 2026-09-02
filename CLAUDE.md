# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Didactic demo of Temporal.io capabilities on .NET 8: SAGA compensation, durable waits via Signal
and Update, and retryable vs non-retryable Activity errors. Domain (orders/inventory/payment) is
kept deliberately simple — see README.md for the full walkthrough of the two test scenarios
(Signal approved / Signal rejected).

## Build / run

Build the whole solution:
```powershell
dotnet build ReleaseOrderDemo.sln
```

Tests (`test/ReleaseOrder.Tests/`, xUnit + Temporal time-skipping):
```powershell
dotnet test ReleaseOrderDemo.sln
```
The suite covers README Pruebas **A–F** for `ReleaseOrderWorkflow` (SAGA + Signal/Update + its
`ShippingWorkflow` child) against the real workflows + Activities + Services; only the SQL edge
(`IOrderStateMachine`, the repos) is faked in memory (`Fakes/`), so no Docker or SQL Server is
needed — 13 tests in ~0.8s. Test classes: `ReleaseOrderWorkflowTests` (A/B + child happy path),
`ReleaseOrderUpdateDecisionTests` (D — Update result / validator reject / first-decision-wins),
`ReleaseOrderRetryPolicyTests` (E — retryable vs non-retryable), `ReleaseOrderChildWorkflowTests`
(C.2 — child failure propagating to the parent SAGA), `ReleaseOrderIdempotencyTests` (F — replay
probe / retried compensation). `Support/HistoryAssertions.cs` reads the Event History via
`WorkflowService.GetWorkflowExecutionHistoryAsync` (not exposed on `WorkflowHandle` in Temporalio
1.9.0) to count Activity attempts and inspect Child Workflow events;
`Fakes/FakeOrderDatabase.FailAfterEffect(...)` arms a post-effect throw for the F.3 case.
`WorkflowEnvironment.StartTimeSkippingAsync` fast-forwards the two demo `Workflow.DelayAsync`
calls (5s + 10s) in `ReleaseOrderWorkFlow.cs`, so the suite runs in seconds. First run downloads the
time-skipping test server binary (needs network once, then cached in the user profile).
CI still has no test step (`.gitlab-ci.yml` only builds Doxygen docs for GitLab Pages).

Run everything via Docker Compose (from `docker/`):
```powershell
docker compose build --no-cache api release-orden-worker
docker compose up -d --force-recreate api release-orden-worker
docker compose logs --tail=100 release-orden-worker
```
`release-orden-worker` should log `Worker listening on 'release-order-task-queue'...`.

Endpoints once running: Swagger at `http://localhost:5000/swagger`, Temporal UI at
`http://localhost:8233`.

Full stack services (docker/docker-compose.yml): `api` (OrderApi), `crear-orden-worker` and
`release-orden-worker` (both built from the same `ReleaseOrder` project/Dockerfile, distinguished
by `command: ["create"|"release", "worker"]`), `orderreport-worker` (OrderReport), `db` (SQL
Server 2022, seeded from `scripts/db/init.sql` + `fix_orders_products_fk.sql` by the `db-init`
service), `temporal` (auto-setup 1.23.0) + `temporal-db` (Postgres) + `temporal-ui`.

`ReleaseOrder`'s `Program.cs` is a single entrypoint dispatched by argv:
`<workerName> <mode> [args]` — `mode` is `worker` (run a Temporal worker), `release` (start a
`ReleaseOrderWorkflow`), or `create` (start a `CrearOrdenWorkflow`). `workerName` (`release` vs
anything else, e.g. `create`) picks the task queue (`{workerName}-order-task-queue`) and, in
worker mode, which workflow type gets registered.

## Architecture

Five projects under `src/`, all targeting net8.0, referencing `Temporalio` 1.9.0:

- **Contracts** — shared workflow interfaces (`IReleaseOrderWorkFlow.cs`, `IShippingWorkflow.cs`,
  `IOrderReportWorkflow.cs`), DTOs, and repository/service interfaces. Everything else depends on
  this for the workflow contract shape used by both the client (OrderApi) and the worker
  (ReleaseOrder).
- **Common** — generic Temporal plumbing shared by all workers/clients:
  - `WorkerHost.RunAsync<TWorkflow>(taskQueue, serviceProvider, activityTypes, additionalWorkflowTypes)`
    connects a `TemporalClient` and runs a `TemporalWorker`, resolving each activity class from DI
    via `GetRequiredService` (so activity classes must be registered in the DI container).
    `additionalWorkflowTypes` (optional) registers extra `[Workflow]` classes on the same worker —
    used to co-locate a Child Workflow with its parent on one task queue (Child Workflows must be
    registered on whichever worker executes them).
  - `WorkflowStarter.StartAsync<TWorkflow[,TResult]>(taskQueue, expr, idPrefix)` connects and
    starts a workflow one-shot (used by `Program.cs`'s CLI `create`/`release` modes, not by the API).
  - `WorkflowValidator.ValidateWorkflowAsync` — `DescribeAsync` wrapper used by OrderApi's status
    endpoint to distinguish "workflow not found" from other RPC errors before querying it.
- **ReleaseOrder** — the worker process containing both workflows:
  - `Workflows/ReleaseOrderWorkFlow.cs` — the SAGA+Signal workflow. Runs activities in order
    (lookup order → reserve inventory → process payment), pushing a compensation onto a
    `Stack<Func<Task>>` after each reversible step. Then calls
    `Workflow.WaitConditionAsync(() => _decisionReceived)` and blocks (no polling, no thread held)
    until either the `SubmitReleaseDecisionAsync` `[WorkflowSignal]` fires or the
    `SubmitReleaseDecisionUpdateAsync` `[WorkflowUpdate]` is called — same underlying decision
    state, so whichever arrives first wins and the other is ignored (idempotency for
    duplicates/both being sent). The Update path additionally runs
    `[WorkflowUpdateValidator] ValidateSubmitReleaseDecisionUpdate` synchronously before being
    accepted: it rejects (no event written, caller gets the error immediately) unless
    `_status == "Waiting for release decision"` — the Signal path has no equivalent guard and is
    always accepted. On approval, marks the order `Completed`, then starts `ShippingWorkflow` as a
    **Child Workflow** (`Workflow.ExecuteChildWorkflowAsync`, child Workflow Id
    `shipping-order-{orderId}`) to ship the order — still inside the same `try`, so if the child
    exhausts its own retries and fails, that exception is caught exactly like an Activity failure
    and unwinds the same compensation stack; on rejection or any other thrown exception, unwinds
    the compensation stack LIFO and marks the order `Compensated`/`CompensationFailed`/`Failed`.
    `[WorkflowQuery] GetStatus()` exposes the current step string for polling from the API.
  - `Workflows/ShippingWorkflow.cs` — the Child Workflow started by `ReleaseOrderWorkFlow.cs` on
    approval. Single step: runs `ShippingActivities.ShipOrderAsync` with its own `ActivityOptions`
    (independent retry policy from the parent). Registered on the same worker/task queue as
    `ReleaseOrderWorkflow` via `additionalWorkflowTypes` in `Program.cs` — Temporal requires a
    Child Workflow's type to be registered on whatever worker ends up executing it.
  - `Activities/PaymentActivities.cs` — `ProcessPaymentAsync` doubles as the demo's example of
    Temporal retry semantics, keyed off the order `Amount` so both paths are reachable from
    `POST /orders` with no extra plumbing: `Amount == TransientFailureAmount` (999999) throws a
    plain `ApplicationException` to simulate a transient gateway timeout — retryable, so it
    consumes all `MaximumAttempts` of `DefaultOptions` (3, with backoff) before the SAGA
    compensates; `Amount <= 0` (checked in `Services/PaymentService.cs`) simulates a gateway
    decline and throws `Temporalio.Exceptions.ApplicationFailureException` with
    `nonRetryable: true`, which skips retries entirely and compensates on the first failure.
    `PaymentService.FailPayment` is an older test-only flag with the same effect as `Amount <= 0`
    but nothing in the repo sets it — prefer the magic `Amount` values.
  - `Activities/InventoryActivities.cs` — `ReserveInventoryAsync` shows the other way to mark an
    error non-retryable: from the *workflow* side instead of the Activity. The Activity just
    throws a plain, Temporal-agnostic `InventoryUnavailableException` (`Activities/InventoryUnavailableException.cs`)
    when stock is insufficient (real domain check in `Services/InventoryService.ReserveAsync`
    against `Products.Stock` — reachable by creating an order with `Quantity` above the seeded
    stock for that product, no magic value needed). `ReleaseOrderWorkFlow.cs`'s
    `InventoryReserveOptions` lists `nameof(InventoryUnavailableException)` in
    `RetryPolicy.NonRetryableErrorTypes`, so the workflow — not the Activity — decides this
    exception type skips retries. Contrast with `PaymentActivities`, where the Activity itself
    throws `ApplicationFailureException(nonRetryable: true)`.
  - `Workflows/CrearOrderWorkFlow.cs` — a plainer SAGA (no Signal) for order creation: mark
    Created → reserve inventory → mark Completed, compensating to `Failed` on error.
  - `Activities/*` — `InventoryActivities`, `PaymentActivities`, `ShippingActivities`,
    `OrderStatusActivities`, `OrderLookupActivities`, each backed by an `Services/*` implementation
    (`*Repository`/`*Service`) talking to SQL Server via `Microsoft.Data.SqlClient`.
    `ShippingActivities.ShipOrderAsync` (invoked only from the `ShippingWorkflow` Child Workflow)
    has the same magic-value convention as `PaymentActivities`: in `ShippingService.ShipAsync`, an
    order `Address` containing `"FAIL"` (case-insensitive) simulates a failed dispatch — the
    Activity throws, the Child Workflow's own retries run out, and the failure propagates to
    `ReleaseOrderWorkFlow.cs` to demonstrate a Child Workflow failure triggering the parent SAGA's
    compensation.
  - Activities are registered with their concrete type in DI and passed to `WorkerHost.RunAsync`
    by type list (see `Program.cs`) — don't register by interface only, `WorkerHost` resolves the
    concrete `Type` objects directly.
- **OrderReport** — separate worker process/task queue (`report-task-queue`) for a report-only
  workflow (`OrderReportWorkflow` + `OrderReportActivities`/`ReportService`), independent of the
  release SAGA.
- **OrderApi** — ASP.NET Core minimal API (`Program.cs`, top-level statements), the only HTTP
  entrypoint. Holds a singleton `TemporalClient` (connects to `temporal:7233`, overridable via
  `TEMPORAL_HOST`) and a transient `SqlConnection`. Key routes:
  - `POST /orders` / `GET /orders` — raw SQL CRUD against the `Orders` table (bypasses Temporal).
  - `POST /orders/{orderId}/release` — starts `ReleaseOrderWorkflow` with workflow id
    `release-order-{orderId}` on `release-order-task-queue` (this id is stable per order — reuse a
    fresh `orderId` per test run, per README).
  - `POST /orders/{orderId}/release/decision` — signals that workflow with a `ReleaseDecision`
    (`{ approved, reason }`), driving the approve/reject path described above.
  - `POST /orders/{orderId}/release/decision-update` — same decision, sent as a `[WorkflowUpdate]`
    via `ExecuteUpdateAsync` instead of a Signal: synchronous result, and rejected with 400
    (`WorkflowUpdateFailedException`) if the workflow isn't in `"Waiting for release decision"`
    yet — a check the Signal endpoint above cannot perform.
  - `GET /orders/{orderId}/status` — uses `WorkflowValidator` then queries `GetStatus` on the
    workflow handle; falls back to the Temporal execution status if the query RPC times out.
  - `GET /reports/{orderId}` — starts `OrderReportWorkflow` on `report-task-queue` and awaits its
    result synchronously.

## Data

SQL Server schema/seed lives in `scripts/db/init.sql`; `scripts/db/fix_orders_products_fk.sql`
patches the `Orders.ProductId → Products.ProductId` FK on an existing DB (both are run in order by
the `db-init` compose service). `Orders.Status` is the source of truth the workflows write back to
via `OrderStatusActivities`; Temporal's own execution status (`Running`/`Completed`) is a separate,
parallel piece of state surfaced via `WorkflowValidator`/`GetStatus`.

`Orders.Status` doubles as the idempotency marker for Temporal's at-least-once Activity retries
(see `specs/03-idempotencia-por-estado.md`): `IOrderStateMachine` (`ReleaseOrder/Services/OrderStateMachine.cs`)
advances it and applies the matching domain effect (`Products.Stock`, `Shipments` insert) in one
SQL transaction per step, reading `Status` with `UPDLOCK` first — a retry always finds `Status`
already past the step and skips the effect. No separate idempotency-ledger table exists; a prior
`dbo.ProcessedActivities` ledger design was superseded by this approach (see
`specs/02-idempotencia-servicios-actividades.md`).
