# 04 - Patching / versionado de Workflow y drenaje del Worker

**Estado:** Aprobado
**Depende de:** [03-idempotencia-por-estado.md](03-idempotencia-por-estado.md)
**Fecha:** 2026-09-03

**Objetivo:** Agregar al demo el versionado de código de Workflow con `Workflow.Patched` y el
drenaje grácil del Worker ante `SIGTERM`, para mostrar un deploy que no rompe las ejecuciones en
vuelo.

## Por qué existe este spec

La demo ya cubre SAGA, Signal/Update, retries reintentables vs. no-reintentables, Child Workflow e
idempotencia (Pruebas A–G). Falta lo que aparece en cualquier operación real de Temporal: **cambiar
el código de un Workflow mientras hay ejecuciones vivas**, y **apagar un Worker sin cortar el
trabajo en curso**.

`ReleaseOrderWorkflow` es el escenario ideal para lo primero: el
`Workflow.WaitConditionAsync(() => _decisionReceived)` deja la ejecución durablemente abierta,
sin límite de tiempo, hasta que llega la Signal o el Update. Se puede arrancar un release con el
código viejo, redeployar el Worker con código nuevo, y recién entonces mandar la decisión: la
ejecución vieja debe seguir tomando el camino viejo (sin `NonDeterminismError`) y una ejecución
nueva el camino nuevo. `Workflow.Patched("...")` es el mecanismo que Temporal ofrece para eso —
devuelve `true` en ejecuciones nuevas y `false` cuando está reproduciendo una historia que no tiene
el marker del patch.

Para lo segundo: hoy `WorkerHost.RunAsync` recibe un `CancellationToken cancellationToken = default`
(`src/Common/Temporal/WorkerHost.cs:22` y `:59`) que **nunca se cancela**. Un
`docker compose stop release-orden-worker` manda `SIGTERM`, el proceso muere de golpe y las
Activities en vuelo se cortan a la mitad (Temporal las reintenta en otro Worker cuando pasa el
`StartToCloseTimeout`, pero el demo no muestra ningún drenaje ordenado). Cableando la señal a un
`CancellationTokenSource` y fijando `TemporalWorkerOptions.GracefulShutdownTimeout`, el Worker deja
de tomar tareas nuevas y espera a que terminen las Activities en curso antes de cerrar.

## Alcance

**Incluye:**

- **`AuditActivities`** (`src/ReleaseOrder/Activities/AuditActivities.cs`): una sola Activity
  `RecordAwaitingDecisionAsync(int orderId)` que solo hace
  `Console.WriteLine($"[Audit] order {orderId} awaiting release decision")`. **No toca SQL**, no
  usa `IOrderStateMachine` ni ningún `Service`: no interfiere con la idempotencia del spec 03. Se
  registra en DI (`ServiceCollectionExtensions.AddReleaseOrderServices`, `AddTransient`) y se agrega
  a la lista `activityTypes` de `src/ReleaseOrder/Program.cs`.
- **Patch en `ReleaseOrderWorkflow`** (`src/ReleaseOrder/Workflows/ReleaseOrderWorkFlow.cs`):
  inmediatamente antes de `_status = "Waiting for release decision"` /
  `await Workflow.WaitConditionAsync(...)`, un bloque:

  ```csharp
  if (Workflow.Patched("audit-before-decision"))
  {
      await Workflow.ExecuteActivityAsync(
          (AuditActivities a) => a.RecordAwaitingDecisionAsync(orderId),
          DefaultOptions);
  }
  // else: código viejo — no hacía nada acá.
  ```

  Con un comentario didáctico que describe las **tres fases** del ciclo de vida de un patch
  (ver "Modelo de datos"). En este spec se implementa **solo la fase 1**.

- **Drenaje en `WorkerHost.RunAsync`** (`src/Common/Temporal/WorkerHost.cs`, las **dos**
  sobrecargas):
  - Un `CancellationTokenSource` propio, enlazado con el `cancellationToken` recibido.
  - `Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); }` para `Ctrl+C`.
  - `PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => cts.Cancel())` para
    `docker compose stop`.
  - `TemporalWorkerOptions.GracefulShutdownTimeout = TimeSpan.FromSeconds(30)`.
  - `worker.ExecuteAsync(cts.Token)` dentro de un `try` que captura `OperationCanceledException`.
  - Logs: `"Worker draining (SIGTERM received), waiting up to 30s for in-flight activities..."`
    al cancelar y `"Worker stopped cleanly."` al salir.
- **`docker/docker-compose.yml`**: `stop_grace_period: 45s` en `crear-orden-worker`,
  `release-orden-worker` y `orderreport-worker` (mayor que el `GracefulShutdownTimeout` de 30s para
  que Docker no mate el proceso antes de que termine de drenar).
- **Tests** (`test/ReleaseOrder.Tests/ReleaseOrderPatchingTests.cs`, clase nueva, time-skipping,
  mismo patrón que las clases A–F): verifican que una ejecución nueva ejecuta
  `RecordAwaitingDecisionAsync` y escribe el marker del patch en la Event History, y que el string
  final del workflow es el mismo con y sin el paso de auditoría (el patch no cambia el resultado de
  negocio). Se agrega a `Support/HistoryAssertions.cs` un helper para contar
  `MarkerRecordedEventAttributes` por nombre de marker.
- **`README.md`**: **Prueba H** (patching / versionado con ejecución en vuelo + Signal diferida) y
  **Prueba I** (drenaje observable con `docker compose stop`). La tabla de la Prueba G suma la fila
  `ReleaseOrderPatchingTests`.
- **`CLAUDE.md`**: describir el patch en la sección de `ReleaseOrderWorkFlow.cs` y el drenaje en la
  sección de `Common` / `WorkerHost`; sumar la clase de tests a la lista.

**No incluye (fuera de alcance de este spec):**

- **Worker Versioning / Build IDs** (`temporal task-queue update-build-ids`, `UseWorkerVersioning`):
  es el mecanismo oficial de deploy por versión de binario, pero es mucho más pesado que un demo
  didáctico y se solapa conceptualmente con el patching. Otro spec si alguna vez entra.
- **Rotación de task queue (blue/green)**: levantar un segundo Worker en una cola nueva y drenar la
  vieja hasta vaciarla. Suma piezas a `docker-compose.yml` sin agregar nada que el patching + el
  graceful shutdown no muestren ya.
- **`WorkflowReplayer` con historias JSON grabadas**: red de seguridad automatizada contra sacar
  mal un patch. Útil, pero no se ve nada en la Temporal UI y agrega un artefacto de test que hay
  que regenerar a mano. Otro spec.
- **Ejecutar `Workflow.DeprecatePatch("audit-before-decision")` o borrar el patch**: las fases 2 y
  3 del ciclo quedan **documentadas** en el README, no ejecutadas. El `if/else` queda como material
  didáctico permanente.
- **Patch en `ShippingWorkflow`, `CrearOrdenWorkflow` u `OrderReportWorkflow`**: solo se versiona
  `ReleaseOrderWorkflow`, que es el único que queda abierto lo suficiente para atraparlo en vuelo.
- **Tabla de auditoría en SQL** (`dbo.AuditLog` + script en `db-init`): la Activity nueva es
  solo-log a propósito, para no tocar el esquema ni la máquina de estados del spec 03.
- **Drenaje del proceso `api`**: es un `WebApplication` de ASP.NET Core con su propio host y ya
  maneja `SIGTERM`; este spec solo toca los Workers.

## Modelo de datos

Este spec **no introduce estructuras de datos nuevas** ni toca el esquema SQL. `Orders.Status` y
sus transiciones siguen exactamente como en `specs/03-idempotencia-por-estado.md`.

Lo único "nuevo" que se persiste es en la **Event History de Temporal**, no en la base:

- **Identificador de patch:** el string literal `"audit-before-decision"`. Es el `patchId` que se
  pasa a `Workflow.Patched(...)` y luego a `Workflow.DeprecatePatch(...)`. Debe ser estable: cambiarlo
  equivale a un patch distinto.
- **Marker en la historia:** la primera vez que una ejecución evalúa `Workflow.Patched("audit-before-decision")`
  y devuelve `true`, Temporal escribe un evento `MarkerRecorded` con ese `patchId`. El SDK .NET usa
  el sdk-core, así que el `MarkerName` es `"core_patch"` (los SDK Go/Java legacy lo llaman
  `"Version"`); además se emite un `UpsertWorkflowSearchAttributes` con `TemporalChangeVersion`. En
  la reproducción, `Patched(...)` devuelve `true` solo si ese marker está en la historia; si no está
  (ejecución arrancada con el código viejo), devuelve `false` y el `else` corre.

Ciclo de vida del patch (didáctico; en este spec **solo se implementa la fase 1**):

| Fase | Código                                                                    | Cuándo                                                                | Qué hace                                                                                             |
| ---- | ------------------------------------------------------------------------- | --------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| 1    | `if (Workflow.Patched("audit-before-decision")) { nuevo } else { viejo }` | al desplegar el cambio                                                | ejecuciones nuevas → rama nueva; ejecuciones viejas en vuelo → rama vieja, sin `NonDeterminismError` |
| 2    | `Workflow.DeprecatePatch("audit-before-decision"); nuevo` (sin `else`)    | cuando ya no queda **ninguna** ejecución vieja abierta ni consultable | sigue escribiendo el marker para históricas ya cerradas, pero el código asume siempre la rama nueva  |
| 3    | `nuevo` (sin patch ni deprecate)                                          | cuando ni siquiera se van a reproducir historias viejas               | código limpio, el patch desaparece                                                                   |

## Plan de implementación

1. **`AuditActivities` + DI + `Program.cs`.** Crear `src/ReleaseOrder/Activities/AuditActivities.cs`
   con `[Activity] RecordAwaitingDecisionAsync(int orderId)` solo-log. Registrar
   `services.AddTransient<AuditActivities>()` en `ServiceCollectionExtensions` y agregar
   `typeof(AuditActivities)` al array `activityTypes` de `Program.cs`. Compila y el Worker arranca;
   la Activity todavía no se usa. Commit.
2. **Patch en `ReleaseOrderWorkFlow.cs`.** Agregar el bloque
   `if (Workflow.Patched("audit-before-decision")) { await Workflow.ExecuteActivityAsync(...); }`
   justo antes de `_status = "Waiting for release decision"`, con el comentario de las tres fases.
   Compila; una ejecución nueva loguea `[Audit] ...` antes de esperar la decisión. Commit.
3. **Drenaje en `WorkerHost.RunAsync`.** En las dos sobrecargas: `CancellationTokenSource` enlazado,
   `Console.CancelKeyPress`, `PosixSignalRegistration` para `SIGTERM`,
   `GracefulShutdownTimeout = 30s` en `TemporalWorkerOptions`, `try/catch (OperationCanceledException)`
   alrededor de `worker.ExecuteAsync(cts.Token)`, y los dos logs de drenaje. `Ctrl+C` local ya
   corta ordenado. Commit.
4. **`docker-compose.yml`.** `stop_grace_period: 45s` en los tres servicios worker. Commit.
5. **Tests.** Helper `CountMarkers(string markerName)` en `Support/HistoryAssertions.cs`. Clase
   `ReleaseOrderPatchingTests` con al menos: (a) una corrida Signal-aprobada que asserta
   `history.CountMarkers("core_patch") == 1` y una llamada a `RecordAwaitingDecisionAsync` en la
   historia; (b) que el string final es el de la Prueba A (el paso de auditoría no altera el
   resultado). Commit.
6. **README.** Prueba H y Prueba I nuevas; fila `ReleaseOrderPatchingTests` en la tabla de la
   Prueba G; actualizar el conteo de tests. Commit.
7. **`CLAUDE.md`.** Patch en la descripción de `ReleaseOrderWorkFlow.cs`; drenaje en la de
   `WorkerHost`; clase de tests en la lista. Commit.
8. **Verificación end-to-end** (ver "Criterios de aceptación"). Secuencia:
   `docker compose up -d` con el Worker **sin** el paso 2 → `POST /orders` → `POST /orders/{id}/release`
   → esperar a `"Waiting for release decision"` → aplicar el paso 2, `docker compose build --no-cache
release-orden-worker && docker compose up -d --force-recreate release-orden-worker` → `POST
/orders/{id}/release/decision` `{approved:true}` → confirmar en Temporal UI que **esa** ejecución
   completó **sin** un `ActivityTaskScheduled` de `RecordAwaitingDecisionAsync` y **sin**
   `WorkflowTaskFailed` por no-determinismo → lanzar un release nuevo con otro `orderId` y ver que
   **sí** ejecuta la auditoría y escribe el marker.

## Criterios de aceptación

- [ ] `dotnet build ReleaseOrderDemo.sln` compila sin errores.
- [ ] `dotnet test ReleaseOrderDemo.sln` pasa, incluida la clase `ReleaseOrderPatchingTests`.
- [ ] Un release **nuevo** (Worker con el patch) registra en la Event History un `MarkerRecorded`
      con `MarkerName = "core_patch"` (el `patchId` `"audit-before-decision"` va en los `Details`) y
      un `ActivityTaskCompleted` de `RecordAwaitingDecisionAsync`, y termina en `Completed` con el
      mismo string que la Prueba A.
- [ ] Un release **arrancado antes** de desplegar el patch, que recibe la Signal **después** del
      redeploy, completa en `Completed` **sin** `WorkflowTaskFailed` / `NonDeterminismError` y
      **sin** ningún evento de `RecordAwaitingDecisionAsync` en su historia.
- [ ] `docker compose stop release-orden-worker` mientras una Activity está en vuelo (p. ej. durante
      el `Workflow.DelayAsync(10s)` previo al Child Workflow, o una Activity real en curso) escribe
      en los logs `Worker draining (SIGTERM received)...` y luego `Worker stopped cleanly.`, y la
      Activity en curso llega a completar antes de que el proceso salga.
- [ ] Durante ese stop, la ejecución del Workflow permanece en `Running` en la Temporal UI y
      retoma y completa al hacer `docker compose up -d release-orden-worker`.
- [ ] `Ctrl+C` sobre `dotnet run` del Worker en local produce el mismo drenaje ordenado (mismos dos
      logs) en vez de un corte abrupto.
- [ ] Las Pruebas A–G del README siguen pasando sin cambios de comportamiento.

## Decisiones tomadas y descartadas

- **Sí:** parchear un **paso nuevo antes del `WaitConditionAsync`**. Es el único punto donde la
  ejecución queda abierta sin límite, así que es el único donde se puede, con comodidad, arrancar
  con código viejo, redeployar y recién después mandar la Signal.
- **No:** parchear el `Workflow.DelayAsync(10s)` previo al Child Workflow o el `ShippingWorkflow`.
  El primero es menos visible en la historia; el segundo dura segundos y es casi imposible
  atraparlo en vuelo para el demo.
- **Sí:** la Activity nueva es **solo-log**. No toca SQL, así que no interactúa con
  `IOrderStateMachine` ni obliga a revisar la tabla de transiciones del spec 03.
- **No:** escribir en una tabla `dbo.AuditLog` nueva ni reusar `OrderStatusActivities` para meter
  un estado `AwaitingApproval`. Lo primero suma esquema, script de `db-init` y repo; lo segundo
  agrega un estado a la máquina del spec 03 y obliga a repasar todas las guardas de "ya aplicado".
- **Sí:** mostrar el camino viejo con una **ejecución en vuelo + Signal diferida**, no con dos
  imágenes Docker. Aprovecha que el Workflow queda abierto indefinidamente y no duplica el
  despliegue.
- **No:** `WorkflowReplayer` con una historia JSON grabada en el repo. Es determinístico y no
  necesita Docker, pero no se ve nada en la Temporal UI y agrega un artefacto que hay que
  regenerar a mano cada vez que cambia el Workflow. Queda para otro spec.
- **Sí:** implementar **solo la fase 1** del patch (`if/else`) y documentar las fases 2
  (`DeprecatePatch`) y 3 (borrado) en el README. El `if/else` es el estado más ilustrativo y
  permanente para un demo; ejecutar `DeprecatePatch` requeriría además garantizar que no quedan
  ejecuciones viejas, lo cual no aporta a lo didáctico.
- **Sí:** drenaje con `GracefulShutdownTimeout` + señal → `CancellationToken` en `WorkerHost`,
  compartido por los tres Workers desde un solo archivo. Cambio contenido y transversal.
- **No:** Worker Versioning con Build IDs. Es el mecanismo oficial de deploy por versión de
  binario, pero pesa mucho para un demo y se solapa con el patching, que ya cubre el "no romper lo
  que está corriendo".
- **No:** rotación de task queue blue/green. Más servicios en `docker-compose.yml` sin mostrar nada
  que el graceful shutdown + el patch no muestren ya.

## Riesgos identificados

| Riesgo                                                                                                                                                                                                              | Mitigación                                                                                                                                                   |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Olvidar registrar `AuditActivities` en DI o en `activityTypes` de `Program.cs`: el Worker lanza al arrancar (`GetRequiredService` falla) o el Workflow queda colgado buscando una Activity que ningún Worker sirve. | Paso 1 del plan hace el registro **antes** de usar la Activity; el criterio de aceptación de arranque del Worker lo cubre.                                   |
| `stop_grace_period` menor que `GracefulShutdownTimeout`: Docker manda `SIGKILL` antes de que el Worker termine de drenar y el demo no muestra el cierre limpio.                                                     | `stop_grace_period: 45s` > `GracefulShutdownTimeout: 30s`. Ambos valores quedan documentados juntos en el spec y en `CLAUDE.md`.                             |
| Reutilizar un `orderId` entre corridas (una orden ya en `Completed` nunca vuelve a reservar) rompe la Prueba H igual que rompía la F.                                                                               | Limitación heredada del spec 03; la Prueba H repite la exigencia del README de usar `orderId` fresco por corrida.                                            |
| El `Workflow.DelayAsync(TimeSpan.FromSeconds(5))` marcado como temporal en `ReleaseOrderWorkFlow.cs:61-64` acorta la ventana para redeployar el Worker mientras la ejecución está viva.                             | La ventana real es el `WaitConditionAsync` posterior, que no tiene límite; el delay de 5s solo retrasa el arranque, no la espera de la decisión.             |
| `PosixSignalRegistration` / `PosixSignal.SIGTERM` requiere `System.Runtime` moderno; en Windows `SIGTERM` no existe pero `Ctrl+C` (`CancelKeyPress`) sí.                                                            | Registrar `SIGTERM` dentro de un `try` que no rompa en Windows; el drenaje local se prueba con `Ctrl+C`, el de contenedor con `docker compose stop` (Linux). |

## Qué NO entra en este spec

- Worker Versioning / Build IDs.
- Rotación de task queue blue/green.
- `WorkflowReplayer` con historias grabadas.
- Ejecutar `Workflow.DeprecatePatch` o borrar el patch (fases 2 y 3).
- Patch en `ShippingWorkflow`, `CrearOrdenWorkflow` u `OrderReportWorkflow`.
- Tabla de auditoría en SQL.
- Drenaje del proceso `api`.

Cada uno, si entra, va en su propio spec.
