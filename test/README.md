# Guía didáctica: los tests de `ReleaseOrderWorkflow` con Temporal

> Documento de acompañamiento de `test/ReleaseOrder.Tests/`. Explica **qué se construyó, por qué,
> qué hay que entender de testear con Temporal, y qué partes de estos tests dependen de código de
> producción** (que es donde están las trampas).

---

## 1. Para qué sirve esta carpeta

La demo tiene dos formas de verificarse:

| Forma | Cómo | Cuánto tarda | Qué necesita |
| --- | --- | --- | --- |
| **Manual** (README, Pruebas A–F) | `docker compose up`, `POST /orders`, mirar Temporal UI y SQL | minutos por escenario | Docker, SQL Server, red |
| **Automatizada** (esta carpeta) | `dotnet test` | **~0.8 s los 13 tests** | nada (solo la 1ª vez baja un binario) |

Los tests **no reemplazan** la demo manual: la demo es para *ver* Temporal (el Event History en la
UI, los reintentos en vivo). Los tests son para que **los escenarios de la demo no se rompan en
silencio** cuando alguien toque el workflow. Cubren las **Pruebas A–F del README** — es decir, todo
`ReleaseOrderWorkflow`: el SAGA, la Signal, el Update, el Child Workflow y la idempotencia.

```powershell
dotnet test ReleaseOrderDemo.sln          # los 13
dotnet test --filter "ReleaseOrderRetryPolicyTests"   # una sola clase
```

---

## 2. El problema de fondo: ¿cómo se testea esto?

`ReleaseOrderWorkflow` es incómodo de testear por tres razones a la vez:

1. **Espera 15 segundos de reloj** (`Workflow.DelayAsync(5s)` + `Workflow.DelayAsync(10s)`), más el
   backoff de los reintentos (1 s + 2 s).
2. **Se queda bloqueado esperando una decisión humana** (`Workflow.WaitConditionAsync`) que llega
   desde afuera por Signal o Update.
3. **Escribe en SQL Server** en cada paso.

Un test ingenuo tardaría casi un minuto por escenario y necesitaría una base. Temporal resuelve
los tres puntos, y esas soluciones son **lo principal que hay que entender de este apartado**:

### 2.1 Time-skipping: el reloj virtual

```csharp
await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();
```

Levanta un **servidor de Temporal embebido en el proceso del test** con un reloj virtual. Cuando el
workflow se duerme en un `Workflow.DelayAsync(10s)`, el servidor no espera 10 segundos: adelanta el
reloj y sigue. Lo mismo con el backoff entre reintentos.

**La regla que hay que tener en la cabeza:** el salto automático de tiempo (*auto time-skipping*)
solo ocurre **mientras el cliente está bloqueado esperando el resultado del workflow**
(`GetResultAsync`). Si el test hace otra llamada al servidor (una Query, un Update), el reloj **se
frena** mientras esa llamada está en vuelo.

De ahí salen dos técnicas opuestas que se usan a propósito en esta suite:

- **Para dejar correr el tiempo:** no hacer nada. Los tests de la Tanda 2
  (`ReleaseOrderRetryPolicyTests`) pasan `drive: _ => Task.CompletedTask` — el workflow falla antes
  de esperar la decisión, así que no hay nada que conducir y el auto-skip se traga el delay de 5 s
  y el backoff solo.
- **Para congelar el tiempo a propósito:** hacer una llamada. El test
  `UpdateRechazadoPorValidador_CuandoNoEstaEsperando` manda el Update **sin esperar nada**: el
  workflow está frenado en el `DelayAsync(5s)` inicial y el reloj está suspendido, así que la
  ventana para probar el rechazo del validador no depende del reloj real. Es determinístico, no una
  carrera.

Y cuando hay que avanzar el tiempo *a mano* (porque estamos haciendo Queries en un bucle), se usa
`env.DelayAsync(...)`, que empuja el reloj virtual — es lo que hace `WaitForStatusAsync`.

### 2.2 El worker de test: workflows y Activities **reales**

```csharp
using var worker = new TemporalWorker(env.Client, new TemporalWorkerOptions(taskQueue)
    .AddAllActivities(new InventoryActivities(inventory))
    .AddAllActivities(new PaymentActivities(payment, stateMachine))
    // ...
    .AddWorkflow<ReleaseOrderWorkflow>()
    .AddWorkflow<ShippingWorkflow>());
```

Esto es **el mismo worker que corre en producción**, con las mismas clases. No hay mocks del
workflow ni de las Activities ni de los Services: `InventoryService`, `PaymentService` y
`ShippingService` son las clases de `src/`. **Lo único falso es el borde SQL.**

Es una decisión deliberada: si mockeáramos las Activities, el test verificaría que el test funciona,
no que el SAGA funciona. Acá si alguien cambia el orden de los pasos o se olvida de apilar una
compensación, el test se cae.

> Nota: `ShippingWorkflow` **tiene** que registrarse en el mismo worker. Temporal exige que el tipo
> de un Child Workflow esté registrado en el worker que lo va a ejecutar. Es el mismo motivo por el
> que `Program.cs` lo pasa en `additionalWorkflowTypes`.

### 2.3 El "drive": empujar al workflow desde afuera

```csharp
return await worker.ExecuteAsync(async () =>
{
    var handle = await env.Client.StartWorkflowAsync(...);   // 1. arranca
    await drive(ctx);                                        // 2. lo empuja (Signal/Update)
    var result = await handle.GetResultAsync();              // 3. espera el final
});
```

`worker.ExecuteAsync(fn)` corre el worker mientras dura `fn` y lo apaga después. Dentro, el patrón
es siempre el mismo: arrancar → conducir → esperar. El **`drive`** es el callback que cada test
provee para mandar la Signal o el Update en el momento justo.

---

## 3. Mapa de la carpeta

```
test/ReleaseOrder.Tests/
├─ Support/
│  ├─ ReleaseOrderTestEnvironment.cs   ← el runner: monta env + worker + drive, devuelve todo lo observable
│  └─ HistoryAssertions.cs             ← lee el Event History (contar intentos, eventos de Child Workflow)
├─ Fakes/                              ← el ÚNICO borde falso: SQL
│  ├─ FakeOrderDatabase.cs             ← la "base" en memoria + superficie de aserciones + sonda de replay
│  ├─ FakeOrderStateMachine.cs         ← espeja la máquina de estados T-SQL de OrderStateMachine.cs
│  ├─ FakeOrderRepository.cs
│  ├─ FakeProductRepository.cs
│  └─ FakeShipmentRepository.cs
├─ ReleaseOrderWorkflowTests.cs        ← Pruebas A, B, C.1
├─ ReleaseOrderUpdateDecisionTests.cs  ← Prueba D
├─ ReleaseOrderRetryPolicyTests.cs     ← Prueba E
├─ ReleaseOrderChildWorkflowTests.cs   ← Prueba C.2
└─ ReleaseOrderIdempotencyTests.cs     ← Prueba F
```

Un solo `.csproj`, con `ProjectReference` a `src/ReleaseOrder` y `src/Contracts`, y `Temporalio`
fijado en **1.9.0** para coincidir con `src/*`. `Temporalio.Testing` (el time-skipping) viene dentro
de ese mismo paquete: no es un NuGet aparte.

---

## 4. El runner: `Support/ReleaseOrderTestEnvironment.cs`

Es la pieza que todos los tests reusan. Su firma:

```csharp
public static async Task<ReleaseOrderRunResult> RunAsync(
    Action<FakeOrderDatabase> seed,          // qué hay en la "base" antes de arrancar
    int orderId,                             // la orden a liberar
    Func<ReleaseOrderDriveContext, Task> drive)  // cómo empujar el workflow
```

y lo que devuelve — **todo lo observable de una corrida**:

```csharp
public sealed record ReleaseOrderRunResult(
    string              Result,       // el string que devolvió el workflow
    FakeOrderDatabase   Db,           // estado final de la "base": Status, Stock, Shipments, llamadas
    string              FinalStatus,  // última Query GetStatus()
    HistoryAssertions   History,      // Event History del PADRE
    ShippingChildFacts? Child);       // el Child Workflow, si llegó a arrancar
```

### Detalle importante: por qué la historia se lee *adentro*

```csharp
var result      = await handle.GetResultAsync();
var finalStatus = await handle.QueryAsync(wf => wf.GetStatus());
var history     = await HistoryAssertions.FetchAsync(env.Client, handle.Id, handle.ResultRunId);
var child       = await TryFetchChildFactsAsync(env.Client, orderId);
```

El `await using var env` **destruye el servidor embebido** al salir de `RunAsync`. Si el test
intentara pedir la historia después, ya no habría a quién preguntarle. Por eso todo lo que se quiera
observar se captura acá y viaja dentro del `record`.

### Detalle importante: el Child Workflow se busca por Id

```csharp
var childId = $"shipping-order-{orderId}";
try
{
    var description  = await client.GetWorkflowHandle(childId).DescribeAsync();
    var childHistory = await HistoryAssertions.FetchAsync(client, childId, runId: null);
    return new ShippingChildFacts(childId, description.Status, childHistory);
}
catch (RpcException e) when (e.Code == RpcException.StatusCode.NotFound)
{
    return null;   // el padre falló antes de despachar: es el caso esperado, no un error
}
```

No hace falta haber guardado el handle del hijo: su Workflow Id es **determinístico**
(`shipping-order-{orderId}`, definido en `ReleaseOrderWorkFlow.cs`). Eso permite describirlo y leer
su historia *desde afuera*, que es justamente lo que demuestra que es una **Workflow Execution
independiente** y no una Activity más del padre.

### `ReleaseOrderDriveContext`: los helpers del drive

| Método | Qué hace |
| --- | --- |
| `WaitForStatusAsync(target)` | Consulta la Query `GetStatus()` en bucle, empujando el reloj con `env.DelayAsync(1s)` entre intentos, hasta ver `target`. Falla con mensaje claro si nunca llega. |
| `SubmitDecisionSignalAsync(d)` | `SignalAsync(...)`. No devuelve resultado de negocio. |
| `SubmitDecisionUpdateAsync(d)` | `ExecuteUpdateAsync(...)`. **Sí** devuelve el string de negocio, síncrono. |
| `ExpectUpdateRejectedAsync(d)` | Espera que el `[WorkflowUpdateValidator]` rechace; devuelve la excepción para asertar sobre su causa. |

Los dos primeros son simétricos a propósito: hacen que el contraste Signal-vs-Update se lea de
inmediato en el cuerpo del test.

---

## 5. `Support/HistoryAssertions.cs` — leer el Event History

### Por qué existe

Para la Prueba E hay que **contar cuántas veces se intentó una Activity**. No se puede contar desde
los fakes: con `Amount = 999999`, `PaymentActivities.ProcessPaymentAsync` lanza **antes** de llamar
a `PaymentService`, así que un contador en el fake nunca se incrementaría. La única fuente de verdad
es el Event History de Temporal.

### Las dos rarezas que resuelve

**(a) En Temporalio 1.9.0 el `WorkflowHandle` NO tiene `FetchHistoryAsync()`.** Ese método se agregó
en versiones posteriores. Se usa la llamada gRPC cruda:

```csharp
var response = await client.WorkflowService.GetWorkflowExecutionHistoryAsync(
    new GetWorkflowExecutionHistoryRequest
    {
        Namespace = client.Options.Namespace,
        Execution = new WorkflowExecution { WorkflowId = workflowId, RunId = runId ?? "" },
        NextPageToken = pageToken,
    });
```

> Si algún día se sube el paquete de Temporalio, esto se puede simplificar a `handle.FetchHistoryAsync()`.

**(b) Contar eventos `ActivityTaskStarted` NO mide los intentos.** El servidor trata los eventos de
los intentos intermedios como *transient*: en la historia final suele quedar **un solo**
`ActivityTaskStarted`, con el número real de intento en su campo `Attempt`. Por eso:

```csharp
public int AttemptsFor(string activityName)
{
    // ...
    var byCount   = started.Count;                    // si el server escribió un Started por intento
    var byAttempt = started.Max(a => a.Attempt);       // si escribió uno solo con Attempt = N
    return Math.Max(byCount, byAttempt);               // robusto a las dos codificaciones
}
```

El `Math.Max` no es paranoia decorativa: es lo que hace que la aserción `== 3` sea fiable sin
depender de un detalle interno del test server.

### La API

| Método | Para qué |
| --- | --- |
| `AttemptsFor("ProcessPayment")` | Cuántas veces se intentó esa Activity |
| `FailureErrorTypeFor("ProcessPayment")` | El `errorType` del último fallo (`"PaymentDeclined"`, `"InventoryUnavailableException"`) |
| `ContainsEventType(EventType.ChildWorkflowExecutionFailed)` | Eventos de Child Workflow en la historia del padre |

> Los nombres de Activity en la historia van **sin** el sufijo `Async` (`ProcessPayment`, no
> `ProcessPaymentAsync`). El helper acepta las dos formas para que nadie se trabe con eso.

---

## 6. Los fakes: el único borde falso

### `FakeOrderStateMachine` — el espejo del T-SQL

Es el fake más delicado, porque **reproduce a mano un contrato que vive en SQL**. La tabla de
transiciones está documentada en su propio comentario y sale de
`specs/03-idempotencia-por-estado.md` y del T-SQL de `ReleaseOrder/Services/OrderStateMachine.cs`:

| Paso | Status destino | "Ya aplicado" si Status ∈ | Efecto |
| --- | --- | --- | --- |
| `ReserveInventory` | `InventoryReserved` | InventoryReserved, PaymentProcessed, Completed, Shipped | `Stock -= qty` |
| `ProcessPayment` | `PaymentProcessed` | PaymentProcessed, Completed, Shipped | (ninguno) |
| `ShipOrder` | `Shipped` | Shipped | `INSERT Shipments` |
| `CancelInventory` | `InventoryCanceled` | InventoryCanceled, Compensated, CompensationFailed | `Stock += qty` |
| `RefundPayment` | `PaymentRefunded` | PaymentRefunded, InventoryCanceled, Compensated | (ninguno) |

**Si el T-SQL cambia, este fake tiene que cambiar con él** — si no, los tests siguen en verde
probando un comportamiento que la producción ya no tiene. Es la deuda consciente de no usar
Testcontainers con SQL Server.

### `FakeOrderDatabase` — base en memoria **y** superficie de aserciones

Guarda `Orders`, `Stock`, `Shipments`, y además dos listas que son puro instrumental de test:

- **`StatusHistory`** — cada transición de `Orders.Status` en orden. Permite asertar el recorrido
  completo de una corrida en una sola línea, que es la aserción más expresiva de toda la suite:
  ```csharp
  Assert.Equal(
      new[] { "Created", "InventoryReserved", "PaymentProcessed", "Completed", "Shipped" },
      run.Db.StatusHistory);
  ```
- **`StateMachineCalls`** — cada llamada a un `Try*Async`, en orden. Sirve para dos cosas: probar el
  **orden LIFO** de la compensación (`refundIdx < cancelIdx`) y contar llamadas repetidas en los
  tests de idempotencia.

> La distinción clave: `StatusHistory` cuenta **efectos aplicados**; `StateMachineCalls` cuenta
> **intentos**. Que uno diga 2 y el otro diga 1 *es* la prueba de idempotencia.

### La sonda de replay: `FailAfterEffect`

Para la Prueba F.3 hay que forzar que una compensación se reintente. El detalle que hace que el test
valga algo:

```csharp
// FakeOrderStateMachine.TryCancelInventoryAsync
_db.Stock[productId] = _db.Stock.GetValueOrDefault(productId) + quantity;
_db.SetStatus(orderId, "InventoryCanceled");

// La sonda lanza ACÁ, con el efecto ya aplicado y el Status ya avanzado.
_db.ThrowIfProbeArmed(nameof(TryCancelInventoryAsync));

return Task.FromResult(StepOutcome.Applied);
```

**Tiene que fallar *después* de aplicar el efecto.** Si fallara antes, el test solo probaría que el
retry funciona. Fallando después, reproduce la ventana exacta que el *at-least-once* de Temporal
explota, y la aserción `Stock == 10` tiene sentido: **sin la guarda de "ya aplicado", el segundo
intento sumaría otra vez y quedaría en 12.**

Es la misma ventana que `PaymentActivities.ReplayProbeAmount` (888888) fuerza en producción para el
camino feliz.

---

## 7. Qué fija cada clase de test

| Clase | Prueba | La lección |
| --- | --- | --- |
| `ReleaseOrderWorkflowTests` | A, B, C.1 | La Signal destraba el `WaitConditionAsync`. Aprobada → completa y despacha; rechazada → **compensación LIFO** (refund *antes* que cancel) y stock restaurado exacto. |
| `ReleaseOrderUpdateDecisionTests` | D | El Update devuelve **resultado de negocio síncrono** (la Signal no). El `[WorkflowUpdateValidator]` puede **rechazar sin dejar evento** en la historia (la Signal no puede). Y "la primera decisión gana" vale para las dos vías. |
| `ReleaseOrderRetryPolicyTests` | E | **3 vs 1**: error genérico → 3 intentos; `ApplicationFailureException(nonRetryable: true)` → 1 (lo decide la **Activity**); `NonRetryableErrorTypes` → 1 (lo decide el **Workflow**). Y `Failed` vs `Compensated` según si la pila de compensación tenía algo. |
| `ReleaseOrderChildWorkflowTests` | C.1, C.2 | El hijo tiene **Id, historia y `RetryPolicy` propios**. Cuando agota *sus* 3 intentos y falla, el padre lo trata igual que un fallo de Activity propia: mismo `catch`, misma compensación. |
| `ReleaseOrderIdempotencyTests` | F | La máquina de estados se consulta 2 veces pero **el efecto se aplica 1 sola vez**, tanto en el camino feliz (replay probe) como en la compensación reintentada. |

### El contraste como técnica

Casi todos los tests están escritos en pares que se leen juntos:

- `AttemptsFor("ProcessPayment") == 3` (E.1) vs `== 1` (E.2) — reintentable vs no.
- `AttemptsFor("ShipOrder") == 3` (C.2) vs `== 1` (C.1) — hijo que falla vs hijo feliz.
- `StateMachineCalls.Count(...) == 2` vs `StatusHistory.Count(...) == 1` (F) — intentos vs efectos.
- `FinalStatus == "Compensated"` (E.1/E.2) vs `== "Failed"` (E.3) — pila con algo vs pila vacía.

Un número solo no enseña nada; el par sí.

---

## 8. ⚠️ De qué depende esta suite fuera de `test/`

**Esta es la sección importante para mantenimiento.** Los tests no son autónomos: se apoyan en cosas
concretas de `src/`. Si alguna cambia, se caen (o peor: pasan sin probar nada).

### 8.1 Valores mágicos de producción

Los tests **no inyectan fallos por reflexión ni por mocks**: usan los mismos disparadores que la demo
manual expone por HTTP. Están definidos en `src/`:

| Disparador | Definido en | Lo usa |
| --- | --- | --- |
| `Amount = 999999` (`TransientFailureAmount`) | `PaymentActivities.cs:22` | E.1 — fallo transitorio reintentable |
| `Amount = 888888` (`ReplayProbeAmount`) | `PaymentActivities.cs:28` | F.1 — replay probe |
| `Amount <= 0` | `PaymentService.cs` | E.2 — rechazo no-reintentable |
| `Quantity > Stock` | `InventoryService` / máquina de estados | E.3 — sin stock |
| `Address` contiene `"FAIL"` | `ShippingService.cs:30` | C.2 — despacho fallido |

**Si se borra un valor mágico "porque es feo", el test correspondiente deja de tener disparador.**

### 8.2 Strings de estado acoplados

Los tests comparan strings literales que vienen del workflow:

- `"Waiting for release decision"` — lo usa `WaitForStatusAsync` en casi todos los tests **y** el
  `[WorkflowUpdateValidator]` para decidir si acepta el Update. Un typo acá rompe las dos cosas.
- `"Completed"`, `"Compensated"`, `"CompensationFailed"`, `"Failed"` — la rama del ternario de
  `ReleaseOrderWorkFlow.cs:146`.
- `"Decision accepted: order will be completed."` — el string exacto que devuelve el Update.
- `"not waiting for a decision"` — subcadena del mensaje del validador.
- Los Status de dominio (`InventoryReserved`, `PaymentProcessed`, `Shipped`, …) — compartidos entre
  el workflow, la máquina de estados y `FakeOrderStateMachine`.

### 8.3 Los `Workflow.DelayAsync` — la dependencia más frágil

`ReleaseOrderWorkFlow.cs:61-64` tiene esto:

```csharp
// TEMPORAL: delay de prueba para poder probar el rechazo del
// validador del Update mientras el estado no es "Waiting for
// release decision". Revertir luego de probar.
await Workflow.DelayAsync(TimeSpan.FromSeconds(5));
```

**Ese delay de 5 s es la ventana que usan dos tests:**

- `UpdateRechazadoPorValidador_CuandoNoEstaEsperando` — manda el Update mientras el workflow todavía
  está en `"Loading order"`.
- `SignalDuplicada_PrimeraGana` — manda las dos Signals dentro de esa ventana para que se
  bufericen juntas, sin carrera.

Y el `DelayAsync(10s)` de la línea 117 (antes del Child Workflow) es lo que le da margen a
`UpdateYSignal_PrimeraGana` para mandar la Signal tardía con el workflow todavía vivo.

> **Si se revierte ese delay "de prueba" (como el comentario sugiere), esos tres tests hay que
> replantearlos.** Está anotado también en el XML doc de `ReleaseOrderUpdateDecisionTests`.

### 8.4 El Id fijo del Child Workflow

`shipping-order-{orderId}` está hardcodeado en `ReleaseOrderWorkFlow.cs` y el runner lo reconstruye
para buscar al hijo. Si el Id cambia de formato, `run.Child` queda siempre `null` y los tests de la
Tanda 3 fallan.

Corolario: **cada test necesita su propio `orderId`**, porque ese Id es único por orden. Rangos en
uso: `1001-1002` (A/B), `1003-1006` (D), `1007-1009` (E), `1010-1011` (C), `1012-1013` (F). La task
queue sí es única por corrida (`$"release-order-test-{Guid.NewGuid():N}"`), pero el Workflow Id no
puede serlo.

### 8.5 Los `RetryPolicy`

Las aserciones `== 3` cuentan `MaximumAttempts` declarados en producción:
`DefaultOptions` y `InventoryReserveOptions` en `ReleaseOrderWorkFlow.cs` (3),
`CompensationOptions` (5), y el `DefaultOptions` propio de `ShippingWorkflow.cs:16` (3). Cambiar
cualquiera rompe la aserción del número — que es exactamente lo que se quiere: es un cambio de
comportamiento que merece que alguien lo confirme.

### 8.6 La versión de Temporalio

`1.9.0`, fijada para coincidir con `src/*`. De ahí sale el rodeo del `WorkflowService` en
`HistoryAssertions` (§5). Subir el paquete permite simplificarlo.

---

## 9. Checklist: qué rompe esta suite

- [ ] Quitar o mover el `Workflow.DelayAsync(5s)` inicial → 3 tests a replantear.
- [ ] Cambiar el texto de un `_status` → falla `WaitForStatusAsync` (mensaje claro: *"El workflow
      nunca llegó a 'X'"*).
- [ ] Cambiar un `MaximumAttempts` → falla la aserción de `AttemptsFor`.
- [ ] Cambiar el T-SQL de `OrderStateMachine.cs` sin actualizar `FakeOrderStateMachine` →
      **los tests siguen en verde y dejan de probar la realidad**. El más peligroso de la lista.
- [ ] Reordenar los pasos del SAGA o no apilar una compensación → falla el `StatusHistory` exacto.
- [ ] Reusar un `orderId` entre tests → choque de Workflow Id del hijo.
- [ ] Mover la sonda `ThrowIfProbeArmed` a *antes* del efecto → el test F.3 sigue verde pero deja
      de probar idempotencia.

---

## 10. Qué NO cubre (y por qué)

| Fuera de alcance | Motivo |
| --- | --- |
| `CrearOrdenWorkflow` | Es otro workflow. Además su `DefaultOptions` no declara `RetryPolicy`, así que con el default del servidor (intentos ilimitados) un `InventoryUnavailableException` reintentaría para siempre y el test se colgaría — habría que tocar producción primero. |
| `OrderReportWorkflow` | Otro workflow y otro proyecto. `OrderReportActivities` recibe un `TemporalClient` **concreto**, no `ITemporalClient`, así que no se puede instanciar desde el test sin refactor. |
| Los batches T-SQL de `OrderStateMachine.cs` (`UPDLOCK`, decremento relativo) | Los reemplaza `FakeOrderStateMachine`. Cubrirlos de verdad exige Testcontainers con SQL Server. |
| Endpoints de `OrderApi` | Bloqueado por tres cosas: no hay `public partial class Program` (no sirve `WebApplicationFactory`), se inyecta `SqlConnection` concreto, y el `TemporalClient` apunta a `temporal:7233` hardcodeado. Sería un refactor, no un test. |
| Test de determinismo con `WorkflowReplayer` | **Pendiente y opcional.** Es lo único que atraparía cambios que romperían *workflows en vuelo* en producción (reordenar activities, agregar o quitar un `DelayAsync`). Ningún test actual lo detecta, porque todos arrancan de cero. |

---

## 11. Cómo agregar un test nuevo

1. Elegí un `orderId` libre (seguí numerando desde 1014).
2. Escribí el `seed`: `db.SeedOrder(orderId, ProductId, quantity, amount, address, stock)`, más
   `db.FailAfterEffect(...)` si necesitás forzar un reintento.
3. Escribí el `drive`:
   - ¿El workflow falla antes de esperar la decisión? → `_ => Task.CompletedTask`.
   - ¿Necesita una decisión? → `WaitForStatusAsync("Waiting for release decision")` y después
     `SubmitDecisionSignalAsync` / `SubmitDecisionUpdateAsync`.
   - ¿Querés atrapar al workflow en un estado temprano? → llamá **sin** esperar: el reloj está
     congelado mientras tu llamada está en vuelo.
4. Aserta sobre `run`: `Result`, `Db.StatusHistory`, `Db.Stock`, `Db.Shipments`,
   `Db.StateMachineCalls`, `FinalStatus`, `History`, `Child`.
5. Si el test tarda más de ~1 s, algo está esperando tiempo **real**: revisá si estás bloqueando el
   auto time-skipping sin querer.
