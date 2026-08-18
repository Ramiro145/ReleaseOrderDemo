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
3. `WorkflowExecutionSignaled` y el contenido de `ReleaseDecision`.
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

## Alcance intencional

Esta versión no agrega todavía timers ni pruebas de replay. El objetivo es
comprender completamente Signal, Update y Child Workflow antes de incorporar
otra capacidad de Temporal.
