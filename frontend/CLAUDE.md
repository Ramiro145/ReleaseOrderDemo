# CLAUDE.md — frontend

Guía específica del frontend. El `CLAUDE.md` de la raíz (`releaseorder_combined/CLAUDE.md`,
backend .NET/Temporal) sigue vigente al trabajar acá: este archivo **se suma**, no lo reemplaza.
Para el dominio (SAGA, Signal vs Update, errores retryables) ver ese archivo y el `README.md` raíz.

## Qué es

SPA de una sola pantalla que consume la API HTTP de **OrderApi** para operar la demo de Temporal:
listar/crear órdenes, disparar el `ReleaseOrderWorkflow`, enviar la decisión de release por
**Signal** o por **Update**, hacer polling del estado del workflow y pedir el reporte de una orden.
No habla con Temporal ni con SQL Server directamente — todo pasa por OrderApi.

## Diseño — "Mostrador de noche"

La SPA está tematizada como el mostrador de una tienda de periféricos (el seed real de
`Products` es Laptop / Mouse / Keyboard) con una impresora térmica a la vista. Cada orden es un
**ticket de papel**; el histórico del `ReleaseOrderWorkflow` se **imprime** paso a paso en ese
ticket y, si el SAGA compensa, se estampa `ANULADO` sobre los pasos revertidos. Es la metáfora
del carrito de compras fusionada con la traza del workflow durable.

- Fondo `--color-counter` (verde-negro); única superficie clara: `--color-paper` (tickets y
  slips). Primario `--color-ledger` ("en verde" = éxito); rechazo/compensación `--color-inkred`
  ("en rojo"). Foco de teclado siempre en `--color-brass`.
- Tipos: `--font-display` Bricolage Grotesque (rótulos), `--font-sans` Public Sans (UI),
  `--font-mono` Martian Mono (ticket, códigos, montos, timestamps). Cargadas por `<link>` en
  `index.html`.
- Utilidades del ticket en `src/index.css`: `.receipt-edge` (borde perforado), `.thermal-grain`,
  `.leader` (línea de puntos concepto→monto), `.trace-line` / `.trace-wait` (animación de
  impresión, respetan `prefers-reduced-motion`), `.stamp` / `.stamp-void` (sellos de goma).
- Los botones de `ui/Button` viven sobre el mostrador oscuro; los del ticket (Confirmar /
  Rechazar / Enviar a preparación) se estilan inline en `DecisionGate` porque van sobre papel.
  `ErrorBanner` toma `onPaper` para el mismo motivo.

## Stack

- **Vite 8** + **React 19** + **TypeScript** (`type: module`, sin React Compiler).
- **Tailwind CSS v4** vía `@tailwindcss/vite` — sin `tailwind.config.js`; el tema se define con
  `@theme` en `src/index.css`. Los estilos son clases utilitarias inline en el JSX.
- **oxlint** como linter (`.oxlintrc.json`), no ESLint.
- Sin librería de routing, estado global, data-fetching ni tests. `fetch` nativo, hooks propios.

## Comandos

```powershell
npm install
npm run dev      # servidor de desarrollo Vite (HMR)
npm run build    # tsc -b && vite build  -> dist/
npm run lint     # oxlint
npm run preview  # sirve dist/ ya compilado
```

No hay `npm test` (no existe suite). `npm run build` corre el type-check completo (`tsc -b`), así
que un error de tipos rompe el build.

## Configuración

- **`VITE_API_URL`** — URL base de OrderApi. Default `http://localhost:5000` (ver
  `src/api/client.ts` y `.env.example`). Copiar `.env.example` a `.env` para cambiarla.
  El backend en Docker Compose expone la API en el puerto 5000.

## Estructura de `src/`

- **`api/client.ts`** — wrapper de `fetch`. Exporta `apiClient.get/post` y la clase `ApiError`
  (`status`, `body`). Normaliza los errores del backend: lee `error` / `detail` / `title` del body
  JSON. `status: 0` = no se pudo conectar. Todo llamado a la API debe pasar por acá.
- **`api/orders.ts`** — una función por endpoint de OrderApi (`listOrders`, `createOrder`,
  `releaseOrder`, `sendReleaseDecisionSignal`, `sendReleaseDecisionUpdate`, `getOrderStatus`,
  `getOrderReport`). Si se agrega una ruta en OrderApi, agregar acá su función.
- **`types/dtos.ts`** — interfaces TS **espejadas a mano** de los DTOs de `Contracts` en el
  backend (`OrderDto`, `CreateOrderRequest`, `ReleaseDecision`, `OrderStatusResponse`,
  `OrderReportResult`). Si cambia un DTO en .NET, actualizar acá.
- **`catalog.ts`** — catálogo de la tienda espejado a mano de `scripts/db/init.sql` (Products).
  `productName(id)` / `productTag(id)`, con caída a `Producto #id` si el id no está sembrado.
- **`lib/workflowSteps.ts`** — traduce los strings crudos de `_status` del `ReleaseOrderWorkFlow`
  (`Reserving inventory`, `Waiting for release decision`, `Compensating`, …) a etiquetas en
  español + glifo + `kind` para el ticket. Si cambia un `_status` en el backend, actualizar acá.
- **`lib/format.ts`** — `formatAmount` (ARS, sin decimales) y `formatClock`.
- **`hooks/useOrders.ts`** — carga y refresca la lista de órdenes.
- **`hooks/useOrderStatus.ts`** — polling de `GET /orders/{id}/status` cada 2500 ms; se detiene
  solo al llegar a un estado terminal (`Completed`, `Compensated`, `CompensationFailed`,
  `Failed`) o ante un error. Expone `restart` para re-arrancarlo tras una acción. Acepta un
  callback opcional `onResult(result)` (leído por ref) que se dispara en cada tick.
- **`hooks/useReceiptTrace.ts`** — sobre `useOrderStatus`; acumula los pasos observados en una
  lista ordenada (`TraceLine[]`) y rellena los pasos intermedios de la secuencia feliz que el
  polling se haya salteado. Expone además `notStarted` (404 = workflow sin arrancar) y
  `compensated`. Se monta por pedido (App le pasa `key={orderId}` a `OrderReceipt`).
- **`components/`**:
  - `Header` — nombre de la tienda + pill de conexión + botón actualizar.
  - `Counter` — el mostrador: selector de producto (radios del catálogo), cantidad, importe a
    cobrar (editable, prefill sugerido; se deja tocar para probar 999999 / 0), dirección,
    `<details>` con atajos para provocar fallos, subtotal y "Realizar pedido" (`createOrder`).
    El `orderCode` se autogenera (`ORD-xxxxxx`).
  - `OrdersLedger` — "Mis pedidos": cada orden es un slip de papel sobre el mostrador; el
    seleccionado lleva borde `ledger-bright`. Scroll interno, sin paginación.
  - `OrderReceipt` — el ticket (contenedor, `Card variant="paper"` + `.receipt-edge`). Cabecera
    con `orderCode`/`#id` + `StatusBadge`, línea de ítem con `.leader`, y debajo `ReceiptTrace`,
    `DecisionGate` y `ReceiptSummary` separados por reglas punteadas.
  - `ReceiptTrace` — imprime las `TraceLine`; el paso en espera late (`.trace-wait`); si
    `compensated`, superpone el sello `ANULADO` (`.stamp-void`).
  - `DecisionGate` — reemplaza a `ReleaseOrderPanel`. Según el paso actual muestra: "Enviar a
    preparación" (`releaseOrder`) si no arrancó; o el form de decisión (radios Signal/Update +
    motivo + "Confirmar pedido" / "Rechazar", vía `sendReleaseDecisionSignal` /
    `sendReleaseDecisionUpdate`). Fuera de la espera avisa que el Update puede dar 400. En
    estado terminal no renderiza nada.
  - `ReceiptSummary` — "Ver comprobante" (`getOrderReport`), render mono al pie del ticket.
  - `ErrorBanner` — toma `error: ApiError | null` y `onPaper?` (mostrador oscuro vs. papel).
- **`components/ui/`** — primitivas presentacionales sin lógica de negocio: `Button`
  (`primary` / `danger` / `quiet`, para el mostrador oscuro), `Card` (`panel` / `paper`),
  `StatusBadge` (sello de goma sobre papel).
- **`App.tsx`** — grid de dos columnas: izquierda `Counter` + `OrdersLedger`, derecha el
  `OrderReceipt` del pedido seleccionado (o un ticket vacío). No hay router ni modal: la orden
  seleccionada es estado local (`selectedOrderId`).
- **`main.tsx`** — entrypoint, monta `<App/>` en `#root` con `StrictMode`.

## Convenciones

- Los textos de UI están en **español**.
- El data-fetching vive en hooks (`useX`) que devuelven `{ data, loading, error, ...acciones }`;
  los componentes no llaman a `api/` directamente salvo acciones puntuales (crear, liberar,
  enviar decisión) en el propio handler.
- Errores: capturar en el handler, guardar como `ApiError` en estado y renderizar con
  `<ErrorBanner error={...} />` (no `alert`, no throw sin capturar).
- Estilos: clases Tailwind inline. Para algo reutilizable, un componente en `components/ui/`.
- `verbatimModuleSyntax` está activo: importar tipos con `import type { ... }`.
- El flujo de decisión de release ofrece los dos mecanismos (Signal / Update) en
  `DecisionGate` — mantener ambos; el Update puede devolver 400 si el workflow todavía no
  está en `"Waiting for release decision"` y eso se muestra como `ApiError` sobre el ticket.
