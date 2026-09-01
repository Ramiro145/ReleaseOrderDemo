# 03 - Idempotencia por estado de la orden (sin tabla de ledger)

**Estado:** Implementado
**Depende de:** [02-idempotencia-servicios-actividades.md](02-idempotencia-servicios-actividades.md) (lo reemplaza)
**Fecha:** 2026-09-01

**Objetivo:** Reemplazar el ledger SQL (`dbo.ProcessedActivities` + `IIdempotencyLedger` +
`IdempotentActivity`) del spec 02 por idempotencia basada en `dbo.Orders.Status`, usando una
transacción atómica por paso en vez de dos mecanismos superpuestos (ledger + guardas de estado
natural).

## Por qué existe este spec

El spec 02 resolvió el at-least-once de Temporal con un ledger SQL más guardas de estado natural
como defensa en profundidad, aceptando explícitamente una ventana no atómica entre la escritura de
dominio y la del ledger (riesgo documentado en ese spec). Para un demo didáctico esto son dos
técnicas superpuestas, una tabla extra, un script SQL extra en `db-init`, y esa ventana sigue
siendo una fuente real de doble efecto si el Worker cae en el momento exacto.

`dbo.Orders.Status` ya es una máquina de estados: `Created → InventoryReserved →
PaymentProcessed → Completed → Shipped`, con `InventoryCanceled`/`PaymentRefunded`/`Compensated`
en la compensación. Si la transición de estado y el efecto de dominio (`Products.Stock`,
`dbo.Shipments`) se escriben en la **misma transacción SQL**, ese estado alcanza como marca de
idempotencia sin necesitar una tabla aparte, y la ventana no atómica desaparece por construcción.

## Alcance

**Incluye:**

- Nuevo contrato `IOrderStateMachine` en `Contracts/Repositories/` con `enum StepOutcome
  { Applied, AlreadyApplied, InsufficientStock, OrderNotFound }` y un método `Try*Async` por paso:
  `TryReserveInventoryAsync`, `TryCancelInventoryAsync`, `TryMarkPaymentProcessedAsync`,
  `TryMarkPaymentRefundedAsync`, `TryShipAsync`.
- Implementación `OrderStateMachine` en `ReleaseOrder/Services/`: cada método ejecuta un único
  batch T-SQL que (1) lee `Orders.Status` con `UPDLOCK, ROWLOCK`, (2) si ya está en un estado que
  implica el paso aplicado, hace `COMMIT` y devuelve `AlreadyApplied`, (3) si no, aplica el efecto
  de dominio y avanza `Status` en la misma transacción, devolviendo `Applied`.
- `InventoryService.ReserveAsync`/`CancelAsync` y `ShippingService.ShipAsync` delegan en
  `IOrderStateMachine` en vez de hacer su propio read-modify-write sobre `Products.Stock` o
  `Shipments`.
- `PaymentActivities.ProcessPaymentAsync`/`RefundPaymentAsync` llaman a `PaymentService` (que ya
  es naturalmente idempotente sobre su `HashSet`) y luego usan `IOrderStateMachine` solo para el
  avance atómico de `Orders.Status`.
- Eliminación de `IdempotentActivity`, `IIdempotencyLedger`, `IdempotencyLedger`,
  `dbo.ProcessedActivities` y `scripts/db/add_idempotency_ledger.sql` (y su línea en `db-init` de
  `docker/docker-compose.yml`).
- Eliminación de los métodos que quedan sin uso: `IProductRepository.UpdateStockAsync`,
  `IShipmentRepository.InsertAsync`/`ExistsForOrderAsync`.
- Valor mágico de demo `ReplayProbeAmount` (`888888`) adaptado: en `ProcessPaymentAsync`, tras
  avanzar `Orders.Status` a `PaymentProcessed` en el primer intento (`Attempt == 1`), se lanza
  `ApplicationException` (reintentable); el segundo intento encuentra el `Status` ya avanzado y no
  duplica el efecto.
- Sección de idempotencia en `README.md` actualizada para describir la máquina de estados en vez
  del ledger.

**No incluye (fuera de alcance de este spec):**

- Cambios a `RetryPolicy`, al stack de compensación LIFO o a la semántica SAGA/Signal/Update.
- Persistencia real de `PaymentService` en la tabla `Payments` — sigue en memoria; su idempotencia
  de actividad sigue viniendo del `HashSet` (`Add`/`Remove`/`Contains`), no de
  `IOrderStateMachine`.
- Migración/limpieza de `dbo.ProcessedActivities` en volúmenes existentes: no se agrega script de
  `DROP TABLE`; queda huérfana hasta que se recree el volumen (`docker compose down -v`).
- `OrderStatusActivities.UpdateOrderStatusAsync` (escribe `Completed`/`Compensated`/`Failed`): un
  `UPDATE` a un valor fijo ya era idempotente en el spec 02 y sigue sin cambios.
- Resolver que reusar un `orderId` entre corridas dé falsos "ya aplicado" — limitación heredada del
  spec 02, documentada en el README con la misma recomendación de usar `orderId` fresco.
- Corregir los bugs preexistentes detectados en `OrderRepository` (`AddAsync` con 7 columnas y 6
  valores; `GetAllAsync` con `DateTime.Parse` sobre una columna `datetime`).

## Modelo de datos

Sin tabla nueva. Transiciones sobre `dbo.Orders.Status` (columna ya existente):

| Paso                | Estado destino      | "Ya aplicado" si `Status` ∈                                      | Efecto de dominio en la misma tx |
| -------------------- | ------------------- | ------------------------------------------------------------------ | --------------------------------- |
| `ReserveInventory`  | `InventoryReserved` | `InventoryReserved`, `PaymentProcessed`, `Completed`, `Shipped`    | `Products.Stock -= qty`           |
| `ProcessPayment`    | `PaymentProcessed`  | `PaymentProcessed`, `Completed`, `Shipped`                         | (ninguno — pago en memoria)       |
| `ShipOrder`         | `Shipped`            | `Shipped`                                                           | `INSERT dbo.Shipments`            |
| `CancelInventory`   | `InventoryCanceled` | `InventoryCanceled`, `Compensated`, `CompensationFailed`           | `Products.Stock += qty`           |
| `RefundPayment`     | `PaymentRefunded`   | `PaymentRefunded`, `InventoryCanceled`, `Compensated`               | (ninguno)                         |

Contrato (`src/Contracts/Repositories/IOrderStateMachine.cs`):

```csharp
public enum StepOutcome { Applied, AlreadyApplied, InsufficientStock, OrderNotFound }

public interface IOrderStateMachine
{
    Task<StepOutcome> TryReserveInventoryAsync(int orderId, int productId, int quantity);
    Task<StepOutcome> TryCancelInventoryAsync(int orderId, int productId, int quantity);
    Task<StepOutcome> TryMarkPaymentProcessedAsync(int orderId);
    Task<StepOutcome> TryMarkPaymentRefundedAsync(int orderId);
    Task<StepOutcome> TryShipAsync(int orderId, string address);
}
```

Batch T-SQL representativo (`TryReserveInventoryAsync`):

```sql
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @current NVARCHAR(50);
SELECT @current = Status FROM dbo.Orders WITH (UPDLOCK, ROWLOCK) WHERE OrderId = @OrderId;

IF @current IS NULL
    BEGIN ROLLBACK TRANSACTION; SELECT 3; END          -- OrderNotFound
ELSE IF @current IN ('InventoryReserved','PaymentProcessed','Completed','Shipped')
    BEGIN COMMIT TRANSACTION; SELECT 1; END            -- AlreadyApplied: reintento, no restar de nuevo
ELSE
BEGIN
    UPDATE dbo.Products SET Stock = Stock - @Quantity
      WHERE ProductId = @ProductId AND IsActive = 1 AND Stock >= @Quantity;
    IF @@ROWCOUNT = 0
        BEGIN ROLLBACK TRANSACTION; SELECT 2; END      -- InsufficientStock
    ELSE
    BEGIN
        UPDATE dbo.Orders SET Status = 'InventoryReserved', UpdatedAt = GETDATE()
          WHERE OrderId = @OrderId;
        COMMIT TRANSACTION; SELECT 0;                  -- Applied
    END
END
```

`UPDLOCK, ROWLOCK` sobre la fila de `Orders` serializa dos intentos concurrentes del mismo paso sin
locks explícitos en C# — reemplaza al truco de colisión de PK (`SqlException.Number` 2627/2601) del
ledger. El decremento relativo (`Stock = Stock - @Quantity ... AND Stock >= @Quantity`) reemplaza
el read-modify-write no atómico anterior (`SELECT` stock, calcular en C#, `UPDATE` valor absoluto).

## Plan de implementación

1. **Contrato `IOrderStateMachine`.** Agregar el enum `StepOutcome` y la interfaz en
   `Contracts/Repositories/`. Compila sin más cambios.
2. **Implementación `OrderStateMachine`.** Crear `src/ReleaseOrder/Services/OrderStateMachine.cs`
   con `Microsoft.Data.SqlClient` (mismo patrón que los repos existentes: connection string por
   constructor, `SqlConnection` por llamada). Un método privado ejecuta el batch parametrizado y
   castea el `SELECT` final (`int`) a `StepOutcome`; cada método público de la interfaz arma su
   propio batch según la tabla de arriba.
3. **Registrar en DI.** En `ServiceCollectionExtensions.AddReleaseOrderServices`, reemplazar
   `AddTransient<IIdempotencyLedger>` por `AddTransient<IOrderStateMachine>(_ => new
   OrderStateMachine(connectionString))`.
4. **`InventoryService`.** `ReserveAsync` delega en `TryReserveInventoryAsync` y mapea
   `Applied`/`AlreadyApplied` → `true`, `InsufficientStock`/`OrderNotFound` → `false`.
   `CancelAsync` delega en `TryCancelInventoryAsync`. Se eliminan `AlreadyReservedStatuses`, la
   lectura previa de la orden vía `IOrderRepository` y el read-modify-write de `Products.Stock`.
5. **`ShippingService`.** Mantiene el valor mágico `"FAIL"` en `Address` (chequeo antes de tocar la
   BD). El resto del efecto pasa a `TryShipAsync`; se elimina la guarda `ExistsForOrderAsync` y el
   `UpdateStatusAsync("Shipped")` posterior (lo hace la transacción).
6. **`PaymentActivities`.** `ProcessPaymentAsync`: llama a `PaymentService.ProcessAsync` primero
   (rechazo de negocio → `ApplicationFailureException(nonRetryable: true)`, sin tocar `Status`);
   si tiene éxito, llama a `TryMarkPaymentProcessedAsync` para el avance atómico de estado.
   `RefundPaymentAsync` análogo con `TryMarkPaymentRefundedAsync` + `PaymentService.RefundAsync`.
7. **Quitar el ledger de las actividades.** `InventoryActivities`, `PaymentActivities`,
   `ShippingActivities` dejan de recibir `IIdempotencyLedger` por constructor y de envolver su
   cuerpo con `IdempotentActivity.RunAsync`.
8. **Eliminar el ledger.** Borrar `IIdempotencyLedger.cs`, `IdempotencyLedger.cs`,
   `IdempotentActivity.cs`, `scripts/db/add_idempotency_ledger.sql`, y la línea `sqlcmd` de ese
   script en `docker/docker-compose.yml` (servicio `db-init`).
9. **Limpiar repos huérfanos.** Eliminar `IProductRepository.UpdateStockAsync` (y su
   implementación), `IShipmentRepository.InsertAsync`/`ExistsForOrderAsync` (y sus
   implementaciones) — quedan sin ningún llamador tras los pasos 4–5.
10. **Replay probe.** En `PaymentActivities.ProcessPaymentAsync`, tras
    `TryMarkPaymentProcessedAsync` exitoso, si `amount == ReplayProbeAmount` y
    `ActivityExecutionContext.Current.Info.Attempt == 1`, lanzar `ApplicationException`
    (reintentable).
11. **README.** Reescribir la sección de idempotencia (Prueba F): ya no hay ledger; la marca es
    `Orders.Status`, la transición es atómica, y los logs pasan de `[Ledger] ...` a `[State] ...`.
12. **Verificación end-to-end.** Con el stack completo (`docker compose down -v && docker compose
    up -d`), correr: (a) los dos escenarios base (Signal aprobada / rechazada), (b) el escenario
    replay probe, (c) falta de stock, (d) `Address` con `FAIL` (fallo de Child Workflow), (e) un
    reinicio del worker a mitad de `Reserving inventory` para confirmar que no hay doble
    decremento.

## Criterios de aceptación

- [x] `dotnet build ReleaseOrderDemo.sln` compila sin errores.
- [x] `docker compose down -v && docker compose up -d` desde `docker/` no crea
      `dbo.ProcessedActivities` (el script que la creaba ya no existe).
- [x] Un release aprobado normal recorre `Orders.Status`:
      `Created → InventoryReserved → PaymentProcessed → Completed → Shipped`, con `Products.Stock`
      decrementado exactamente `Quantity` y una sola fila en `Shipments` para ese `OrderId`.
- [x] Con `Amount = 888888` (replay probe): el primer intento de `ProcessPaymentAsync` avanza
      `Status` a `PaymentProcessed` y luego lanza; el segundo intento encuentra el `Status` ya
      avanzado y no vuelve a llamar a `PaymentService`; el release termina en `Completed`.
- [x] En el escenario replay probe, `Products.Stock` se decrementa una sola vez y no hay filas
      duplicadas en `Shipments`.
- [x] Reiniciar el worker durante `Reserving inventory` (transacción no confirmada aún, o ya
      confirmada) no produce doble decremento de `Products.Stock`: la atomicidad de la transacción
      garantiza que el efecto y el `Status` se escriben juntos o no se escribe ninguno.
- [x] Una compensación que se reintenta (`CancelInventoryAsync`, `RefundPaymentAsync`) no aplica su
      efecto más de una vez: `Products.Stock` no queda por encima del valor original.
- [x] Los dos escenarios del README (Signal aprobada / Signal rechazada) siguen funcionando igual
      que antes.
- [x] Dos intentos concurrentes del mismo paso sobre la misma orden no aplican el efecto dos veces:
      el `UPDLOCK` serializa el segundo hasta que el primero confirma o revierte.

## Decisiones tomadas y descartadas

- **Sí:** idempotencia basada en `dbo.Orders.Status` con transacción atómica por paso, sin tabla
  de ledger. Menos piezas que el spec 02, y la atomicidad (`UPDLOCK` + efecto + `Status` en una
  sola transacción) es estrictamente más fuerte que "ledger + guardas de estado natural como
  defensa en profundidad" — cierra por construcción la ventana no atómica que el spec 02
  documentaba como riesgo aceptado.
- **No:** mantener el ledger junto a la máquina de estados (ambas técnicas). Duplicaría el trabajo
  sin ganar nada: la transacción atómica ya cubre lo que el ledger cubría, y peor, con menos
  ventanas de fallo.
- **Sí:** `UPDLOCK, ROWLOCK` sobre la fila de `Orders` para serializar intentos concurrentes.
  Equivalente al rol que cumplía la PK de `ProcessedActivities` con captura de 2627/2601, pero sin
  necesitar una tabla ni manejo de excepción SQL específico.
- **No:** clave de idempotencia explícita (`{WorkflowId}:{ActivityType}:{OrderId}`) como en el
  spec 02. Ya no hace falta: el estado de negocio (`Orders.Status`) es la clave, y es más legible
  para quien inspecciona la tabla `Orders` directamente.
- **Sí:** decremento relativo de stock (`Stock = Stock - @Quantity ... AND Stock >= @Quantity`) en
  vez de leer-calcular-escribir en C#. Atómico a nivel de fila; el `@@ROWCOUNT = 0` reemplaza el
  chequeo `product.Stock < quantity` que antes se hacía fuera de la transacción.
- **No:** transacción distribuida que además incluya el estado en memoria de `PaymentService`
  (`HashSet`). Fuera de alcance — ese servicio ya se documenta como naturalmente idempotente y su
  pérdida de estado al reiniciar el worker es una limitación preexistente del spec 02, no agravada
  por este cambio.
- **Sí:** eliminar `IProductRepository.UpdateStockAsync` e `IShipmentRepository.InsertAsync`/
  `ExistsForOrderAsync` en vez de dejarlos sin uso. Evita código muerto que invite a reintroducir
  el patrón read-modify-write por accidente.

## Riesgos identificados

| Riesgo                                                                                                                                      | Mitigación                                                                                                                       |
| ---------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| Reutilizar un `orderId` entre corridas de prueba da falsos "ya aplicado" (una orden en `Completed` nunca vuelve a reservar).                    | Limitación heredada del spec 02; el README exige `orderId` fresco por corrida, nota reforzada en la sección de idempotencia.    |
| `dbo.ProcessedActivities` queda huérfana en volúmenes existentes creados antes de este spec.                                                    | No se agrega `DROP TABLE`; `docker compose down -v` recrea la DB limpia. Documentado en el README y en este spec.               |
| `PaymentService` es `Singleton` en memoria; un reinicio del worker pierde `_processedPayments`, y un refund posterior no encuentra el pago.     | Preexistente del spec 02, no introducido aquí; `Orders.Status` sigue avanzando a `PaymentRefunded` aunque el servicio loguee "no payment found". |
| Un `UPDLOCK` de larga duración (transacción que no confirma) podría bloquear otros pasos sobre la misma orden.                                  | Cada batch es una única ida y vuelta corta (lectura + `UPDATE`s + commit); no hay I/O externo ni espera dentro de la transacción. |
