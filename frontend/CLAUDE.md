# CLAUDE.md — frontend

Guía específica del frontend. El `CLAUDE.md` de la raíz (`releaseorder_combined/CLAUDE.md`,
backend .NET/Temporal) sigue vigente al trabajar acá: este archivo **se suma**, no lo reemplaza.
Para el dominio (SAGA, Signal vs Update, errores retryables) ver ese archivo y el `README.md` raíz.

## Qué es

SPA de una sola pantalla que consume la API HTTP de **OrderApi** para operar la demo de Temporal:
listar/crear órdenes, disparar el `ReleaseOrderWorkflow`, enviar la decisión de release por
**Signal** o por **Update**, hacer polling del estado del workflow y pedir el reporte de una orden.
No habla con Temporal ni con SQL Server directamente — todo pasa por OrderApi.

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
- **`hooks/useOrders.ts`** — carga y refresca la lista de órdenes.
- **`hooks/useOrderStatus.ts`** — polling de `GET /orders/{id}/status` cada 2500 ms; se detiene
  solo al llegar a un estado terminal (`Completed`, `Compensated`, `CompensationFailed`,
  `Failed`) o ante un error. Expone `restart` para re-arrancarlo tras una acción.
- **`components/`** — `OrderList`, `CreateOrderForm`, `OrderDetailPanel` (contenedor con tabs
  "Liberar" / "Reporte"), `ReleaseOrderPanel` (botón liberar + form de decisión Signal/Update),
  `OrderReport`, `ErrorBanner`.
- **`components/ui/`** — primitivas presentacionales sin lógica de negocio: `Button`, `Card`,
  `Modal`, `Tabs`, `StatusBadge`, `Pagination`.
- **`App.tsx`** — layout de dos columnas (lista + detalle) y el modal de creación. No hay router:
  la orden seleccionada es estado local (`selectedOrderId`).
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
  `ReleaseOrderPanel` — mantener ambos; el Update puede devolver 400 si el workflow todavía no
  está en `"Waiting for release decision"` y eso se muestra como `ApiError`.
