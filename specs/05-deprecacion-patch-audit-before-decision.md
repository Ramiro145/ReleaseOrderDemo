# 05 - Deprecación del patch `audit-before-decision` (fase 2)

**Estado:** Borrador
**Depende de:** [04-patching-versionado-drenaje.md](04-patching-versionado-drenaje.md)
**Fecha:** 2026-09-04

**Objetivo:** Ejecutar la fase 2 del ciclo de vida del patch `audit-before-decision`, reemplazando
el `if (Workflow.Patched(...))` por `Workflow.DeprecatePatch(...)` + el paso de auditoría
incondicional, una vez verificado que no quedan ejecuciones pre-patch abiertas.

## Por qué existe este spec

El spec 04 dejó documentado, pero explícitamente fuera de alcance, el ciclo de vida completo del
patch `audit-before-decision`: la fase 1 (`if/else`) quedó implementada como "material didáctico
permanente", y las fases 2 (`Workflow.DeprecatePatch`) y 3 (borrado total) quedaron solo descritas
en el README (§"Ciclo de vida del patch (fases 2 y 3, no implementadas acá)") y en el comentario de
`ReleaseOrderWorkFlow.cs:95-113`.

Ese punto de partida hoy asume que siempre puede haber una ejecución vieja (pre-patch) en vuelo. En
la práctica de este demo eso deja de ser cierto en cuanto se verifica que no queda ninguna ejecución
`Running` de `ReleaseOrderWorkflow` sin el marker `core_patch` en su historia — es exactamente la
condición que Temporal documenta para `DeprecatePatch`: "no longer applicable because all workflows
that use the old code path are done and will never be queried again" (XML doc de
`Temporalio.Workflows.Workflow.DeprecatePatch`, paquete `Temporalio` 1.9.0). Este spec aplica esa
fase 2 dentro del proyecto, sin llegar a la fase 3 (que borraría también `DeprecatePatch` y el
concepto de patch desaparecería del código).

## Alcance

**Incluye:**

- **`src/ReleaseOrder/Workflows/ReleaseOrderWorkFlow.cs:95-113`**: el bloque
  `if (Workflow.Patched("audit-before-decision")) { ... }` pasa a:

  ```csharp
  Workflow.DeprecatePatch("audit-before-decision");
  await Workflow.ExecuteActivityAsync(
      (AuditActivities a) => a.RecordAwaitingDecisionAsync(orderId),
      DefaultOptions);
  ```

  sin condicional ni `else`. El comentario didáctico se reescribe para explicar: qué era la fase 1,
  por qué se pudo avanzar a la fase 2 (precondición verificada), y qué falta para la fase 3 (borrar
  también `DeprecatePatch`, ver "Qué NO entra en este spec").
- **`test/ReleaseOrder.Tests/ReleaseOrderPatchingTests.cs`**: se actualiza la clase existente (no se
  crea una nueva). El XML doc y los nombres de test dejan de hablar de "ejecución nueva vs. vieja
  detrás del patch" y pasan a describir el escenario de "patch deprecado, paso incondicional". Se
  mantienen los asserts `run.History.CountMarkers("core_patch") == 1` y
  `run.History.AttemptsFor("RecordAwaitingDecisionAsync") >= 1` (el marker se sigue escribiendo con
  `DeprecatePatch`, solo cambia su flag `deprecated`), y se agrega un comentario/assert que deje
  explícito que el paso ya no depende de una condición.
- **`README.md`**: reescribir la sección "Ciclo de vida del patch" y la Prueba H:
  - H.1 (ejecución vieja en vuelo + Signal diferida) se conserva **como relato histórico** de cómo
    se llegó a este punto (ya no es reproducible tal cual contra el código actual, que no tiene más
    el `else`).
  - Nueva subsección con el código de la fase 2, la precondición operativa verificable (ver "Plan de
    implementación", paso 1) y qué se observa en la Event History después del deploy.
  - Fase 3 queda documentada como pendiente, igual que en el spec 04.
  - Actualizar la fila `ReleaseOrderPatchingTests` de la tabla de la Prueba G si su descripción
    cambia.
- **`CLAUDE.md`**: la frase actual ("Only phase 1 (`if/else`) is in the code; `DeprecatePatch` and
  removal are documented in README Prueba H, not done") pasa a describir la fase 2 aplicada y la
  fase 3 pendiente.

**No incluye (fuera de alcance de este spec):**

- **Fase 3** (borrar también `Workflow.DeprecatePatch(...)` y dejar solo el paso nuevo, sin ninguna
  mención al patch): se documenta como pendiente, no se ejecuta. Es un cambio de una línea cuando
  llegue el momento, pero borra la última referencia visible al ciclo de vida del patch en el
  código — se prefiere dejarlo como material didáctico un spec más.
- **Cambios a `AuditActivities`**: sigue solo-log, sin SQL, sin tocar `IOrderStateMachine`.
- **Patch o deprecate en `ShippingWorkflow`, `CrearOrdenWorkflow` u `OrderReportWorkflow`**: ninguno
  de los tres tiene un patch vigente.
- **`WorkflowReplayer` con historias JSON grabadas, Worker Versioning / Build IDs, rotación de task
  queue blue/green**: mismos motivos que en el spec 04, no aportan nada nuevo acá.
- **Cambios al drenaje del Worker (`WorkerHost`) o a `docker-compose.yml`**: fuera del alcance de
  este spec, que es puramente sobre el ciclo de vida del patch.

## Modelo de datos

Este spec no introduce estructuras nuevas ni toca el esquema SQL. El único cambio observable es en
la **Event History de Temporal**: el marker `core_patch` para el `patchId` `"audit-before-decision"`
se sigue escribiendo en cada ejecución nueva, pero ahora con el flag `deprecated` activo — refleja
que el código ya no tiene rama vieja que la reproducción pueda tomar. El `patchId` en sí se mantiene
estable (cambiarlo sería, por definición, un patch distinto).

## Plan de implementación

1. **Precondición operativa (sin cambios de código).** Con el stack de Temporal arriba, listar las
   ejecuciones `Running` de `ReleaseOrderWorkflow`:

   ```powershell
   temporal workflow list --query "WorkflowType='ReleaseOrderWorkflow' AND ExecutionStatus='Running'"
   ```

   (o el filtro equivalente en la Temporal UI, `http://localhost:8233`). Confirmar que ninguna es
   pre-patch — es decir, que todas tienen ya el marker `core_patch` en su historia (todas las
   ejecuciones vivas fueron arrancadas después del deploy del spec 04). Registrar el comando y su
   salida en este spec antes de continuar. Si aparece alguna ejecución pre-patch, este spec se
   detiene hasta que esa ejecución termine o se decida explícitamente terminarla — no se avanza a
   la fase 2 con una ejecución vieja abierta.
2. **Fase 2 en el workflow.** Reemplazar el `if (Workflow.Patched(...))` por
   `Workflow.DeprecatePatch("audit-before-decision");` seguido del `ExecuteActivityAsync`
   incondicional, con el comentario reescrito. Compila; una ejecución nueva sigue logueando
   `[Audit] order {id} awaiting release decision`. Commit.
3. **Tests.** Actualizar `ReleaseOrderPatchingTests` (XML doc, nombres de test, comentario del
   assert de incondicionalidad); mantener los dos asserts existentes sobre el marker y la Activity.
   `dotnet test ReleaseOrderDemo.sln` en 15/15. Commit.
4. **README.** Reescribir §"Ciclo de vida del patch" y la Prueba H según lo descrito en "Alcance".
   Commit.
5. **`CLAUDE.md`.** Actualizar la descripción del patch en la sección de `ReleaseOrderWorkFlow.cs`.
   Commit.
6. **Verificación end-to-end.** Rebuild y redeploy del Worker
   (`docker compose build --no-cache release-orden-worker && docker compose up -d --force-recreate
   release-orden-worker`), arrancar un release con `orderId` fresco, aprobarlo, e inspeccionar en la
   Temporal UI que la Event History tiene el `MarkerRecorded` `core_patch` (con `deprecated`), el
   `UpsertWorkflowSearchAttributes` de `TemporalChangeVersion`, y el `ActivityTaskCompleted` de
   `RecordAwaitingDecisionAsync`, sin `WorkflowTaskFailed`.

## Criterios de aceptación

- [ ] `dotnet build ReleaseOrderDemo.sln` compila sin errores.
- [ ] `dotnet test ReleaseOrderDemo.sln` pasa 15/15, incluida la clase `ReleaseOrderPatchingTests`
      actualizada.
- [ ] Antes del deploy, el listado de ejecuciones `Running` de `ReleaseOrderWorkflow` no contiene
      ninguna pre-patch (comando y salida documentados en este spec, paso 1 del plan).
- [ ] En `src/ReleaseOrder/Workflows/ReleaseOrderWorkFlow.cs` no queda ningún
      `Workflow.Patched("audit-before-decision")`, y `Workflow.DeprecatePatch("audit-before-decision")`
      aparece exactamente una vez.
- [ ] Un release nuevo con `orderId` fresco termina en `Completed` con el mismo string y el mismo
      recorrido de `Orders.Status` que la Prueba A, ejecuta `RecordAwaitingDecisionAsync`, y su
      Event History sigue teniendo un `MarkerRecorded` `core_patch` más el
      `UpsertWorkflowSearchAttributes` de `TemporalChangeVersion`.
- [ ] Ninguna ejecución nueva produce `WorkflowTaskFailed` / `NonDeterminismError`.
- [ ] (Recomendado, no bloqueante) Una ejecución arrancada con el código de la **fase 1** y decidida
      **después** del deploy de la fase 2 completa sin `NonDeterminismError`, porque su historia ya
      tiene el marker del patch.
- [ ] Las Pruebas A–G del README siguen pasando sin cambios de comportamiento.

## Decisiones tomadas y descartadas

- **Sí:** avanzar solo a la **fase 2** (`DeprecatePatch`) en este spec, dejando la fase 3 (borrado
  total) documentada como pendiente. Es el paso que el spec 04 dejó explícitamente abierto, y sigue
  siendo observable en la Event History (el marker se sigue escribiendo) — a diferencia de la fase
  3, que borraría toda huella del ciclo de vida del patch en el código.
- **No:** saltar directo a la fase 3. Perdería el paso intermedio, que es justamente el que muestra
  cómo se retira un patch de forma segura sin eliminar de golpe la evidencia en la historia.
- **Sí:** reemplazar el `if/else` in-place y documentar el antes/después (fase 1 → fase 2) en el
  README con ambos bloques de código, en vez de dejar la fase 1 comentada en el propio workflow.
  Mantiene el código de producción limpio; la enseñanza vive en la documentación, que es donde ya
  vive el resto del ciclo de vida del patch (spec 04).
- **No:** dejar el bloque `if (Workflow.Patched(...))` comentado sobre el `DeprecatePatch` como
  referencia histórica en el código. Es código muerto que invita a reintroducir por accidente una
  rama vieja que ya no debería existir.
- **Sí:** actualizar la clase de test existente (`ReleaseOrderPatchingTests`) en vez de crear una
  clase nueva. El escenario que el arnés puede ejercer (el código siempre arranca "nuevo") no
  cambia con `DeprecatePatch`: sigue siendo "corre el paso y escribe el marker", así que no hay
  necesidad de duplicar infraestructura de test.
- **Sí:** exigir como precondición **verificable** (no solo documentada) que no queden ejecuciones
  pre-patch abiertas antes de deployar la fase 2, con el comando y su salida registrados en el
  spec. Es la única garantía real contra el motivo por el que `DeprecatePatch` existe con esa
  advertencia explícita en su propia documentación.
- **No:** terminar (`temporal workflow terminate`) explícitamente ejecuciones viejas que pudieran
  quedar abiertas. Se prefiere bloquear el spec hasta que terminen naturalmente o hasta que el
  usuario decida explícitamente terminarlas fuera de este flujo — `DeprecatePatch` no es reversible
  en el sentido de que una ejecución pre-patch que se reproduzca después ya no tiene rama vieja a
  la que volver.

## Riesgos identificados

| Riesgo | Mitigación |
| --- | --- |
| Queda una ejecución **pre-patch** abierta al deployar la fase 2: al reproducir su historia sin marker, `DeprecatePatch` asume la rama nueva y aparece un `WorkflowTaskFailed` / `NonDeterminismError`. | Paso 1 del plan es la verificación explícita con `temporal workflow list`; es criterio de aceptación bloqueante antes de tocar el código. |
| Se pierde el `if/else` como material didáctico visible en el código de producción. | README Prueba H conserva ambos bloques (fase 1 histórica y fase 2 aplicada) con la explicación del salto entre una y otra. |
| Reutilizar un `orderId` entre corridas de prueba da falsos "ya aplicado" (limitación heredada del spec 03). | El README exige `orderId` fresco por corrida, misma nota que las Pruebas F y H del spec 04. |
| El assert `CountMarkers("core_patch") == 1` podría no seguir siendo válido si el SDK dejara de emitir marker para un patch deprecado. | El paso 3 del plan corre los tests antes de tocar la documentación; si el conteo cambia, se ajusta el assert y se documenta la diferencia en este spec. |

## Qué NO entra en este spec

- Fase 3 del ciclo de vida del patch (borrar `DeprecatePatch` y dejar solo el paso nuevo).
- Cambios a `AuditActivities`.
- Patch o deprecate en `ShippingWorkflow`, `CrearOrdenWorkflow` u `OrderReportWorkflow`.
- `WorkflowReplayer` con historias grabadas, Worker Versioning / Build IDs, rotación blue/green.
- Cambios al drenaje del Worker o a `docker-compose.yml`.

Cada uno, si entra, va en su propio spec.
