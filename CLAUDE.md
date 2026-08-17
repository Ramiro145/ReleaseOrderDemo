# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Didactic demo of two Temporal.io capabilities on .NET 8: SAGA compensation and durable waits via
Signal. Domain (orders/inventory/payment) is kept deliberately simple — see README.md for the
full walkthrough of the two test scenarios (Signal approved / Signal rejected).

## Build / run

No test project or CI test step exists in this repo (`.gitlab-ci.yml` only builds Doxygen docs
for GitLab Pages). There is no `dotnet test` to run.

Build the whole solution:
```powershell
dotnet build ReleaseOrderDemo.sln
```

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

- **Contracts** — shared workflow interfaces (`IReleaseOrderWorkFlow.cs`, `IOrderReportWorkflow.cs`),
  DTOs, and repository/service interfaces. Everything else depends on this for the workflow
  contract shape used by both the client (OrderApi) and the worker (ReleaseOrder).
- **Common** — generic Temporal plumbing shared by all workers/clients:
  - `WorkerHost.RunAsync<TWorkflow>(taskQueue, serviceProvider, activityTypes)` connects a
    `TemporalClient` and runs a `TemporalWorker`, resolving each activity class from DI via
    `GetRequiredService` (so activity classes must be registered in the DI container).
  - `WorkflowStarter.StartAsync<TWorkflow[,TResult]>(taskQueue, expr, idPrefix)` connects and
    starts a workflow one-shot (used by `Program.cs`'s CLI `create`/`release` modes, not by the API).
  - `WorkflowValidator.ValidateWorkflowAsync` — `DescribeAsync` wrapper used by OrderApi's status
    endpoint to distinguish "workflow not found" from other RPC errors before querying it.
- **ReleaseOrder** — the worker process containing both workflows:
  - `Workflows/ReleaseOrderWorkFlow.cs` — the SAGA+Signal workflow. Runs activities in order
    (lookup order → reserve inventory → process payment), pushing a compensation onto a
    `Stack<Func<Task>>` after each reversible step. Then calls
    `Workflow.WaitConditionAsync(() => _decisionReceived)` and blocks (no polling, no thread held)
    until the `SubmitReleaseDecisionAsync` `[WorkflowSignal]` fires. First signal wins; later ones
    are ignored (idempotency for duplicate signals). On approval, marks the order `Completed`; on
    rejection or any thrown exception, unwinds the compensation stack LIFO and marks the order
    `Compensated`/`CompensationFailed`/`Failed`. `[WorkflowQuery] GetStatus()` exposes the current
    step string for polling from the API.
  - `Workflows/CrearOrderWorkFlow.cs` — a plainer SAGA (no Signal) for order creation: mark
    Created → reserve inventory → mark Completed, compensating to `Failed` on error.
  - `Activities/*` — `InventoryActivities`, `PaymentActivities`, `ShippingActivities`,
    `OrderStatusActivities`, `OrderLookupActivities`, each backed by an `Services/*` implementation
    (`*Repository`/`*Service`) talking to SQL Server via `Microsoft.Data.SqlClient`.
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
