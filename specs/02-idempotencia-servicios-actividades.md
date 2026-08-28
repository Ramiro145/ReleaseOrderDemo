# 02 - Idempotencia en servicios y actividades

**Estado:** Borrador
**Depende de:** -
**Fecha:** 2026-08-28

**Objetivo:** Hacer que las actividades de escritura del worker `ReleaseOrder` (reserva/cancelación de inventario, procesamiento/reembolso de pago y despacho) toleren el at-least-once de Temporal sin duplicar su efecto, combinando un ledger de idempotencia en SQL Server con guardas de estado natural en los servicios.

## Por qué existe este spec

Las actividades actuales aplican efectos no idempotentes: `InventoryService.ReserveAsync` resta `Products.Stock`, `CancelAsync` lo suma, `ShippingService.ShipAsync` hace `INSERT` en `Shipments`, y `PaymentService` muta un `HashSet` en memoria. Temporal garantiza *at-least-once*: si el worker escribe en la base y luego cae (o la actividad supera el `StartToCloseTimeout`) antes de reportar la completitud, Temporal reintenta la misma actividad y el efecto se aplica dos veces — stock restado de más, filas de envío duplicadas, compensaciones que restauran stock varias veces (`CompensationOptions` permite hasta 5 intentos). El demo necesita mostrar el patrón estándar para resolver esto.

## Alcance

**Incluye:**

- Nueva tabla `ProcessedActivities` en `OrdersDb` (ledger de idempotencia), creada por un script SQL aditivo.
- Nuevo script `scripts/db/add_idempotency_ledger.sql` y una línea `sqlcmd` extra en el comando del servicio `db-init` de `docker/docker-compose.yml`, después de `init.sql` y `fix_orders_products_fk.sql`.
- Nuevo contrato `IIdempotencyLedger` en `Contracts` y su implementación SQL `IdempotencyLedger` en `ReleaseOrder/Services`.
- Helper `IdempotentActivity` en `ReleaseOrder/Activities` que envuelve la ejecución de una actividad: consulta el ledger por `IdempotencyKey`; si hay hit devuelve el resultado guardado sin llamar al servicio; si no, ejecuta el servicio y persiste `(key, resultado)`.
- Clave de idempotencia compuesta: `{WorkflowId}:{ActivityType}:{OrderId}`, tomada de `ActivityExecutionContext.Current.Info` más el `orderId` del argumento.
- Integración del helper en las actividades de escritura de `ReleaseOrder`:
  - `InventoryActivities.ReserveInventoryAsync`, `InventoryActivities.CancelInventoryAsync`.
  - `PaymentActivities.ProcessPaymentAsync`, `PaymentActivities.RefundPaymentAsync`.
  - `ShippingActivities.ShipOrderAsync`.
- Guardas de estado natural en los servicios, como defensa en profundidad para la ventana no atómica entre la escritura de dominio y la escritura del ledger:
  - `InventoryService.ReserveAsync`: no volver a restar si la orden ya está en `InventoryReserved` (o posterior).
  - `InventoryService.CancelAsync`: no volver a sumar si la orden ya está en `InventoryCanceled`.
  - `ShippingService.ShipAsync`: no volver a insertar si ya existe una fila en `Shipments` para ese `OrderId` (nuevo método `IShipmentRepository.ExistsForOrderAsync`).
  - `PaymentService.ProcessAsync` / `RefundAsync`: ya son naturalmente idempotentes sobre el `HashSet` (`Add`/`Remove` con chequeo de `Contains`); se documenta, no se cambia.
- Manejo de colisión concurrente: `IdempotencyKey` es PK de `ProcessedActivities`; si el `INSERT` lanza violación de clave (`SqlException.Number` 2627 o 2601) se interpreta como "otro intento ganó" y se re-lee el resultado guardado.
- Valor mágico de demo `ReplayProbeAmount` (`888888`) en `PaymentActivities.ProcessPaymentAsync`: en el primer intento (`Attempt == 1`) lanza *después* de que el servicio escribió y el ledger guardó, pero antes de que la actividad reporte completitud, para que el reintento observe el hit del ledger y no duplique el efecto.
- Registro del ledger en `ReleaseOrder/Infrastructure/ServiceCollectionExtensions.cs`.
- Sección nueva en `README.md` describiendo el escenario "replay probe" y qué mirar (logs del ledger, `Products.Stock`, `Shipments`).

**No incluye (fuera de alcance de este spec):**

- Migrar `PaymentService` a la tabla `Payments` (persistencia real de pagos) — sigue en memoria; su idempotencia de actividad la da el ledger.
- Actividades de solo lectura: `OrderLookupActivities.GetOrderAsync`, `InventoryActivities.CheckInventoryAsync` (no mutan estado, no necesitan ledger).
- `OrderStatusActivities.UpdateOrderStatusAsync` (un `UPDATE` a un valor fijo ya es idempotente).
- El proyecto `OrderReport` (otro proceso/worker/task queue) — queda igual.
- Endpoint de replay manual en `OrderApi` — no se agrega superficie HTTP nueva.
- Cambios a los `RetryPolicy`, al stack de compensación LIFO o a la semántica SAGA/Signal/Update documentada en `CLAUDE.md`.
- Transacción SQL compartida entre el servicio y el ledger (escritura de dominio + ledger atómicas). Se acepta la ventana no atómica y se cubre con las guardas de estado natural; unificar en una transacción queda para un spec futuro.
- Idempotencia en `CrearOrdenWorkflow` como criterio de aceptación: queda cubierta incidentalmente porque comparte `InventoryActivities`, pero no se verifica en este spec.

## Modelo de datos

Nueva tabla en `OrdersDb`:

```sql
CREATE TABLE [dbo].[ProcessedActivities] (
    [IdempotencyKey] NVARCHAR(300) NOT NULL,   -- "{WorkflowId}:{ActivityType}:{OrderId}"
    [WorkflowId]     NVARCHAR(200) NOT NULL,
    [ActivityType]   NVARCHAR(100) NOT NULL,
    [OrderId]        INT           NOT NULL,
    [ResultJson]     NVARCHAR(MAX) NULL,        -- resultado serializado; NULL para actividades void
    [CreatedAt]      DATETIME      NOT NULL CONSTRAINT [DF_ProcessedActivities_CreatedAt] DEFAULT (getdate()),
    CONSTRAINT [PK_ProcessedActivities] PRIMARY KEY CLUSTERED ([IdempotencyKey] ASC)
);
```

El script `add_idempotency_ledger.sql` envuelve el `CREATE TABLE` en `IF OBJECT_ID('dbo.ProcessedActivities', 'U') IS NULL` para ser re-aplicable sobre un volumen existente.

Contrato nuevo (`src/Contracts/Repositories/IIdempotencyLedger.cs`):

```csharp
public record LedgerEntry(string IdempotencyKey, string? ResultJson);

public interface IIdempotencyLedger
{
    Task<LedgerEntry?> TryGetAsync(string key);
    // Devuelve false si la key ya existía (colisión concurrente); en ese caso el llamador re-lee con TryGetAsync.
    Task<bool> SaveAsync(string key, string workflowId, string activityType, int orderId, string? resultJson);
}
```

Forma de la clave: `release-order-5:ReserveInventoryAsync:5`. Un reintento del mismo paso reusa la fila; una ejecución nueva del workflow (nuevo `WorkflowId`) genera una clave distinta y vuelve a ejecutar.

## Plan de implementación

1. **Script SQL del ledger.** Crear `scripts/db/add_idempotency_ledger.sql` con el `CREATE TABLE` idempotente de arriba. Agregar la línea `sqlcmd ... -i /docker-entrypoint-initdb.d/add_idempotency_ledger.sql` al final del `command` de `db-init` en `docker/docker-compose.yml`. Verificación: `docker compose up db db-init` crea la tabla; `SELECT * FROM ProcessedActivities` responde vacío.
2. **Contrato `IIdempotencyLedger`.** Agregar `IIdempotencyLedger` y el record `LedgerEntry` en `Contracts/Repositories/`. Compila sin más cambios.
3. **Implementación `IdempotencyLedger`.** Crear `src/ReleaseOrder/Services/IdempotencyLedger.cs` con `Microsoft.Data.SqlClient` (mismo patrón que los repos existentes: connection string por constructor, `SqlConnection` por llamada). `TryGetAsync` hace `SELECT`; `SaveAsync` hace `INSERT` y captura `SqlException` con `Number` 2627/2601 devolviendo `false`. Registrar en `ServiceCollectionExtensions.AddReleaseOrderServices` como `AddTransient<IIdempotencyLedger>(_ => new IdempotencyLedger(connectionString))`.
4. **Helper `IdempotentActivity`.** Crear `src/ReleaseOrder/Activities/IdempotentActivity.cs` con dos sobrecargas estáticas: una para `Func<Task>` (void) y otra para `Func<Task<T>>`. Construye la key desde `ActivityExecutionContext.Current.Info` + `orderId`, llama `TryGetAsync`, y si no hay hit ejecuta el delegado y llama `SaveAsync` (serializando el resultado con `System.Text.Json` en la sobrecarga genérica). Si `SaveAsync` devuelve `false`, re-lee con `TryGetAsync`.
5. **Integrar en `InventoryActivities`.** Envolver el cuerpo de `ReserveInventoryAsync` y `CancelInventoryAsync` con el helper, recibiendo el `IIdempotencyLedger` por constructor (agregar al ctor y al registro DI ya hecho en el paso 3). Verificación: ejecutar un release normal; aparece una fila por actividad en `ProcessedActivities`.
6. **Integrar en `PaymentActivities` y `ShippingActivities`.** Igual que el paso 5 para `ProcessPaymentAsync`, `RefundPaymentAsync` y `ShipOrderAsync`. Verificación: release normal completo deja 3–5 filas de ledger según el camino.
7. **Guardas de estado natural en los servicios.** `InventoryService.ReserveAsync`: leer estado de la orden (vía `IOrderRepository.GetByIdAsync`) y devolver `true` sin restar si ya está en `InventoryReserved`/`PaymentProcessed`/`Completed`/`Shipped`. `InventoryService.CancelAsync`: no sumar si ya está en `InventoryCanceled`. `ShippingService.ShipAsync`: agregar `IShipmentRepository.ExistsForOrderAsync(int orderId)` (y su impl en `ShipmentRepository`) y devolver `true` sin insertar si ya existe. Verificación: forzar una segunda ejecución manual de la actividad (reiniciar el worker) sin fila de ledger y confirmar que el servicio no duplica.
8. **Valor mágico `ReplayProbeAmount`.** En `PaymentActivities.ProcessPaymentAsync`, tras la ejecución idempotente exitosa, si `amount == ReplayProbeAmount` y `ActivityExecutionContext.Current.Info.Attempt == 1`, lanzar `ApplicationException` (reintentable). Verificación: crear orden con `Amount = 888888`, liberar y aprobar; en los logs se ve el intento 1 fallando post-escritura y el intento 2 devolviendo el resultado del ledger; `Products.Stock` bajó una sola vez y no hay pago duplicado.
9. **README.** Documentar el escenario "replay probe" (`Amount = 888888`) junto a los montos mágicos existentes: pasos, líneas de log a buscar (`[Ledger] hit ...`), y cómo verificar `Products.Stock` y `Shipments`.
10. **Verificación end-to-end.** Con el stack completo (`docker compose up`), correr: (a) los dos escenarios del README (Signal aprobada / rechazada) sin regresión, (b) el escenario replay probe, (c) el escenario de falta de stock y el de `Address` con `FAIL` confirmando que las compensaciones no restauran stock de más al reintentarse.

## Criterios de aceptación

- [ ] `dotnet build ReleaseOrderDemo.sln` compila sin errores.
- [ ] Levantar el stack crea la tabla `dbo.ProcessedActivities`; re-levantarlo sobre el volumen existente no falla (script re-aplicable).
- [ ] Un release aprobado normal deja exactamente una fila en `ProcessedActivities` por cada actividad de escritura ejecutada (`ReserveInventoryAsync`, `ProcessPaymentAsync`, `ShipOrderAsync`), con `IdempotencyKey` de la forma `release-order-{id}:{ActivityType}:{id}`.
- [ ] Con `Amount = 888888` (replay probe): el primer intento de `ProcessPaymentAsync` falla después de escribir, el segundo intento devuelve el resultado guardado sin volver a llamar a `PaymentService`, y el release termina en `Completed`.
- [ ] En el escenario replay probe, `Products.Stock` del producto se decrementa una sola vez (no dos) y no hay filas duplicadas en `Shipments` para ese `OrderId`.
- [ ] Reiniciar el worker durante el paso de reserva de inventario (sin fila de ledger todavía) no produce doble decremento de `Products.Stock` gracias a la guarda de estado natural en `InventoryService.ReserveAsync`.
- [ ] Una compensación que se reintenta (`CancelInventoryAsync`, `RefundPaymentAsync`) no aplica su efecto más de una vez: `Products.Stock` no queda por encima del valor original.
- [ ] Los dos escenarios del README (Signal aprobada / Signal rechazada) siguen funcionando igual que antes.
- [ ] Una colisión de `IdempotencyKey` (INSERT duplicado) no lanza excepción no controlada: se captura y se re-lee el resultado guardado.

## Decisiones tomadas y descartadas

- **Sí:** ledger SQL en la capa de actividad **más** guardas de estado natural en los servicios. El título del spec pide "servicios y actividades" y las dos técnicas se complementan: el ledger es el mecanismo fuerte y explícito; las guardas cubren la ventana no atómica entre la escritura de dominio y la del ledger.
- **No:** solo guardas de estado natural (sin tabla). Más simple pero deja la lógica de dedupe dispersa y sin un artefacto observable que sirva de material didáctico.
- **Sí:** clave `{WorkflowId}:{ActivityType}:{OrderId}`. Estable entre todos los reintentos del mismo paso lógico y legible en la tabla. Una nueva ejecución del workflow (nuevo `WorkflowId`) reejecuta, que es el comportamiento deseado para un demo donde se reusa el `orderId`.
- **No:** clave `{WorkflowId}:{ActivityId}`. Deduplica igual pero los `ActivityId` son ids internos de Temporal, menos legibles al inspeccionar la tabla.
- **No:** clave de dominio pura (`{OrderId}:reserve-inventory`). Exige convención manual de nombres y no distingue reejecuciones del workflow para el mismo `orderId`.
- **Sí:** `ProcessedActivities` con PK en `IdempotencyKey` y captura de violación de clave (2627/2601) como "ya procesado". Cubre la carrera de dos intentos concurrentes sin locks explícitos.
- **No:** transacción SQL única que abarque la escritura de dominio y la del ledger. Sería lo correcto en producción, pero los repos abren una `SqlConnection` por llamada y unificar la transacción implica refactor de la capa de datos; se difiere y se compensa con las guardas naturales (que además motivan didácticamente por qué se quieren las dos capas).
- **No:** migrar `PaymentService` a la tabla `Payments`. Amplía el alcance a persistencia real de pagos y un `IPaymentRepository` nuevo; el ledger ya vuelve idempotente la actividad de pago.
- **Sí:** valor mágico `ReplayProbeAmount = 888888` que fuerza un reintento post-escritura. Consistente con `TransientFailureAmount` (999999) y la `Address` con `"FAIL"`; permite observar la idempotencia desde `POST /orders` sin infraestructura extra.
- **No:** endpoint de replay en `OrderApi`. Agrega superficie HTTP para algo que el valor mágico ya cubre.
- **No:** tocar `RetryPolicy`, compensaciones o la semántica SAGA/Signal/Update. Fuera del objetivo; el spec solo agrega dedupe.

## Riesgos identificados

| Riesgo | Mitigación |
| --- | --- |
| Ventana no atómica: la escritura de dominio commitea pero el `SaveAsync` del ledger falla antes de persistir. En el reintento no hay hit de ledger. | Guardas de estado natural en los servicios (`InventoryService`, `ShippingService`): el reintento no duplica el efecto y el ledger se escribe en ese segundo intento. |
| `ResultJson` desincronizado si el tipo de retorno de una actividad cambia en el futuro y quedan filas viejas en el ledger. | El ledger vive en `OrdersDb`, que se reinicializa por `orderId` fresco en cada corrida de prueba (convención del README). En producción se versionaría la key; se documenta como pendiente. |
| El script extra en `db-init` corre antes de que SQL Server esté listo. | Se agrega después de `init.sql`/`fix_orders_products_fk.sql`, que ya esperan el `sleep 20` del comando actual; hereda esa espera. |
| Guarda de estado natural en `InventoryService.ReserveAsync` da falso positivo si un `orderId` se reutiliza y la orden ya quedó en un estado avanzado de una corrida anterior. | El README ya obliga a usar un `orderId` fresco por corrida; se refuerza esa nota en la sección nueva. |
| `PaymentService` es `Singleton` y su `HashSet` se pierde al reiniciar el worker; un refund tras reinicio no encuentra el pago. | Comportamiento preexistente, no introducido por este spec; el ledger hace que la actividad de refund sea idempotente aunque el servicio loguee "no payment found". Se documenta. |
