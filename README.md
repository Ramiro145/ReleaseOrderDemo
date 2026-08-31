# ReleaseOrderDemo — SAGA y Signals con Temporal

Esta versión mantiene el dominio sencillo para observar dos capacidades de
Temporal: compensaciones mediante SAGA y espera durable mediante Signal.

## Flujo didáctico

```text
Leer orden
→ reservar inventario
→ procesar pago
→ esperar una decisión externa
```

Si la Signal aprueba:

```text
decisión aprobada → completar orden
```

Si la Signal rechaza:

```text
decisión rechazada
→ reembolsar pago
→ cancelar inventario
→ marcar orden Compensated
```

## Qué muestra la Signal

- El Workflow permanece `Running` mientras espera.
- `Workflow.WaitConditionAsync` no realiza polling ni ocupa un hilo esperando.
- La Signal modifica el estado interno del Workflow.
- La Signal no devuelve un resultado comercial; confirma que Temporal la aceptó.
- La primera decisión recibida gana y las duplicadas se ignoran.
- El Event History conserva `WorkflowExecutionSignaled`.
- Si el Worker se reinicia, Temporal reconstruye la decisión mediante replay.

## Correcciones consolidadas

- `Contracts` está incluido en la solución.
- Las Activities se registran con su tipo concreto en `WorkerHost`.
- `ProductRepository` coincide con las columnas y tipos reales de `Products`.
- `OrderRepository` lee correctamente la columna `Status`.
- `scripts/db/fix_orders_products_fk.sql` corrige la relación
  `Orders.ProductId → Products.ProductId` en una base existente.

## Levantar la versión nueva

Desde la carpeta `docker`:

```powershell
docker compose build --no-cache api release-orden-worker
docker compose up -d --force-recreate api release-orden-worker
```

Comprueba el Worker:

```powershell
docker compose logs --tail=100 release-orden-worker
```

Debe mostrar:

```text
Worker listening on 'release-order-task-queue'...
```

Swagger está en `http://localhost:5000/swagger` y Temporal UI en
`http://localhost:8233`.

## Prueba A: Signal aprobada

### 1. Crear una orden

```http
POST /orders
```

Utiliza un `productId` existente. Conserva el `orderId` devuelto.

### 2. Iniciar el Workflow

```http
POST /orders/{orderId}/release
```

### 3. Observar la espera

Antes de enviar la decisión, consulta:

```http
GET /orders/{orderId}/status
```

Resultado interno esperado:

```text
Waiting for release decision
```

Temporal UI debe mostrar el Workflow como `Running`.

### 4. Enviar la Signal

```http
POST /orders/{orderId}/release/decision
Content-Type: application/json

{
  "approved": true,
  "reason": "Approved for release"
}
```

Resultado final esperado:

```text
Workflow: Completed
Orden: Completed
```

## Prueba B: Signal rechazada

Crea otra orden y vuelve a iniciar su Workflow. Después envía:

```http
POST /orders/{orderId}/release/decision
Content-Type: application/json

{
  "approved": false,
  "reason": "Manual review rejected the release"
}
```

Resultado esperado:

```text
PaymentRefunded
→ InventoryCanceled
→ Compensated
```

Temporal mostrará el Workflow como `Completed`, porque el rechazo fue manejado
y compensado. El resultado comercial de la orden será `Compensated`.

## Qué comparar en Temporal UI

En el Event History de ambas órdenes identifica:

1. Las Activities de inventario y pago.
2. El momento en que el Workflow queda abierto.
3. `WorkflowExecutionSignaled` (o `WorkflowExecutionUpdateAccepted` si usaste
   `/decision-update`, ver Prueba D) y el contenido de `ReleaseDecision`.
4. La nueva Workflow Task creada por la Signal.
5. En aprobación: `UpdateOrderStatus(Completed)` y el Child Workflow de envío.
6. En rechazo: `RefundPayment`, `CancelInventory` y estado `Compensated`.

Usa una orden diferente para cada recorrido porque el Workflow ID es estable:

```text
release-order-{orderId}
```

## Prueba C: Child Workflow de envío (éxito y fallo)

Tras aprobar la Signal, `ReleaseOrderWorkflow` (parent) inicia `ShippingWorkflow`
(child) para despachar la orden — un Workflow Execution independiente, con su
propio Workflow Id y Event History, anidado bajo el parent en Temporal UI.

### C.1 — Envío exitoso

Repite la Prueba A (Signal aprobada) con una orden cuyo `Address` no contenga
la palabra `FAIL`. Resultado esperado:

```text
Workflow: Completed
Orden: Completed
```

En Temporal UI, dentro del Workflow `release-order-{orderId}` verás un evento
`StartChildWorkflowExecutionInitiated` y podrás navegar al Child Workflow
`shipping-order-{orderId}`, con su propia ejecución de la Activity
`ShipOrderAsync`.

### C.2 — Fallo del Child Workflow → compensación completa

Crea una orden con un `Address` que contenga `FAIL` (por ejemplo
`"FAIL - Calle de prueba"`) y repite la Prueba A. `ShippingService.ShipAsync`
simulará un despacho fallido; la Activity del child reintenta según su propia
`RetryPolicy` y, al agotarla, el Child Workflow falla. Esa falla propaga hacia
`ReleaseOrderWorkflow`, que dispara la misma compensación LIFO que un fallo de
Activity (`RefundPayment` → `CancelInventory`).

Resultado esperado:

```text
Workflow: Completed
Orden: Compensated
```

Esto demuestra que el SAGA del parent no distingue entre un fallo de Activity
propia y un fallo propagado desde un Child Workflow: ambos son simplemente una
excepción dentro del mismo `try`.

## Prueba D: decisión vía Update (en vez de Signal)

`POST /orders/{orderId}/release/decision-update` envía la misma `ReleaseDecision`
pero como `[WorkflowUpdate]` en lugar de Signal, usando `ExecuteUpdateAsync`.
Diferencias observables frente a la Prueba A/B:

- La llamada HTTP espera el resultado de forma síncrona (la Signal solo
  confirma `Accepted`, sin resultado de negocio).
- El Update corre `ValidateSubmitReleaseDecisionUpdate` (`[WorkflowUpdateValidator]`)
  antes de aceptarse: si el Workflow no está en `"Waiting for release decision"`
  (por ejemplo, si ya se envió una decisión antes), lo rechaza sin escribir
  evento en el Event History y la API responde `400` con el error de
  `WorkflowUpdateFailedException`. La Signal no tiene este validador — siempre
  se acepta.
- Si el Workflow no existe, la API responde `404` (`WorkflowValidator` lo
  detecta antes de llamar al Update).
- Signal y Update comparten el mismo estado de decisión interno: cualquiera de
  los dos que llegue primero gana, y el otro queda sin efecto (protección ante
  duplicados o ante enviar ambos).

Repite la Prueba A o B pero usando `/decision-update` en el paso 4 para
comparar el Event History (`WorkflowExecutionUpdateAccepted`/`Completed` en vez
de `WorkflowExecutionSignaled`).

## Prueba E: errores reintentables vs. no-reintentables

Dos formas de marcar un error como no-reintentable en Temporal, contrastadas
con el caso reintentable por defecto:

### E.1 — Pago: error reintentable (timeout transitorio simulado)

Crea una orden con `Amount = 999999` y libérala (Prueba A). `PaymentActivities.ProcessPaymentAsync`
lanza una `ApplicationException` genérica simulando un timeout de gateway.
Temporal la trata como reintentable y agota los `MaximumAttempts` (3, con
backoff) de `DefaultOptions` antes de que el SAGA compense. En el Event
History verás múltiples `ActivityTaskStarted`/`ActivityTaskFailed` para
`ProcessPaymentAsync` antes de `RefundPayment`/`CancelInventory`.

### E.2 — Pago: error no-reintentable desde la Activity

Crea una orden con `Amount <= 0` y libérala. `PaymentService.ProcessAsync`
devuelve `false` (gateway declina) y la Activity lanza
`Temporalio.Exceptions.ApplicationFailureException` con `nonRetryable: true`.
Temporal no reintenta: la compensación arranca en el primer intento fallido.
Contrasta el único `ActivityTaskFailed` aquí contra los múltiples de E.1.

### E.3 — Inventario: error no-reintentable decidido por el Workflow

Crea una orden con `Quantity` mayor al `Stock` sembrado para ese `productId` en
`Products` y libérala. `InventoryActivities.ReserveInventoryAsync` lanza
`InventoryUnavailableException`, una excepción simple sin conocimiento de
Temporal. Es `ReleaseOrderWorkflow` quien decide que no debe reintentarse:
`InventoryReserveOptions` incluye `nameof(InventoryUnavailableException)` en
`RetryPolicy.NonRetryableErrorTypes`. Esto muestra la alternativa a E.2: en vez
de que la Activity se marque a sí misma como no-reintentable, es el Workflow
quien decide por tipo de excepción.

## Prueba F: idempotencia de actividades (replay probe)

Temporal garantiza _at-least-once_: si el Worker escribe en la base y se cae (o
la Activity supera su `StartToCloseTimeout`) **antes** de reportar completitud,
Temporal reintenta la misma Activity y un efecto no idempotente se aplica dos
veces — stock restado de más, filas de envío duplicadas, compensaciones que
restauran stock varias veces.

Las Activities de escritura de `ReleaseOrder` (`ReserveInventoryAsync`,
`CancelInventoryAsync`, `ProcessPaymentAsync`, `RefundPaymentAsync`,
`ShipOrderAsync`) se envuelven con el helper `IdempotentActivity`, que consulta
un ledger SQL (`dbo.ProcessedActivities`) por una clave estable
`release-order-{id}:{ActivityType}:{id}`: si ya hay fila, devuelve el resultado
guardado sin volver a ejecutar el cuerpo. Como defensa en profundidad para la
ventana no atómica entre la escritura de dominio y la del ledger, los servicios
(`InventoryService`, `ShippingService`) tienen además guardas de estado natural
(no vuelven a restar/sumar/insertar si la orden ya pasó por ese paso).

### F.1 — Escenario replay probe con `Amount = 888888`

Crea una orden con `Amount = 888888` (`ReplayProbeAmount`), un `Address` **sin**
la palabra `FAIL`, y libérala y apruébala (Prueba A). En el primer intento de
`ProcessPaymentAsync` el servicio escribe y el ledger guarda la fila, y **acto
seguido** la Activity lanza una `ApplicationException` reintentable simulando la
caída del Worker antes de reportar completitud. Temporal reintenta; el segundo
intento observa el hit del ledger y devuelve el resultado guardado sin volver a
llamar a `PaymentService`. La orden termina en `Completed`.

Líneas de log a buscar en `docker compose logs release-orden-worker`:

```text
[Activity] Payment processed for order {id}          # attempt 1: efecto aplicado
[Ledger] saved release-order-{id}:ProcessPaymentAsync:{id}
[Activity] Replay probe for order {id}: throwing after write+ledger on attempt 1
[Ledger] hit release-order-{id}:ProcessPaymentAsync:{id}, returning stored result   # attempt 2
```

### F.2 — Verificar que el efecto se aplicó una sola vez

- **`Products.Stock`**: anota el stock del `productId` antes de la prueba.
  Después del release aprobado debe haber bajado **exactamente** `Quantity`
  unidades, no `2 × Quantity`.
  ```sql
  SELECT ProductId, Stock FROM Products WHERE ProductId = {productId};
  ```
- **`Shipments`**: debe haber **una sola** fila para ese `OrderId`.
  ```sql
  SELECT * FROM Shipments WHERE OrderId = {orderId};
  ```
- **`ProcessedActivities`**: una fila por Activity de escritura ejecutada
  (`ReserveInventoryAsync`, `ProcessPaymentAsync`, `ShipOrderAsync` en el camino
  aprobado), con `IdempotencyKey` de la forma `release-order-{id}:{ActivityType}:{id}`.
  ```sql
  SELECT IdempotencyKey, OrderId, CreatedAt FROM ProcessedActivities
  WHERE OrderId = {orderId} ORDER BY CreatedAt;
  ```

### F.3 — Compensaciones idempotentes

Repite la Prueba B (Signal rechazada) o la Prueba C.2 (`Address` con `FAIL`): si
`CancelInventoryAsync` o `RefundPaymentAsync` se reintentan, el ledger y las
guardas de estado natural evitan que el stock quede **por encima** del valor
original.

> Usa un `orderId` fresco por corrida: el Workflow Id `release-order-{orderId}`
> es estable y la clave del ledger depende de él, así que reusar un `orderId` de
> una corrida anterior daría hits de ledger o falsos positivos en las guardas de
> estado natural.

## Alcance intencional

Esta versión no agrega todavía timers. El objetivo es comprender completamente
Signal, Update, Child Workflow e idempotencia de actividades antes de incorporar
otra capacidad de Temporal.
