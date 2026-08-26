# 01 - Frontend React/Vite para el demo de Temporal

**Estado:** Implementado
**Depende de:** -
**Fecha:** 2026-08-26

**Objetivo:** Crear un frontend en React/Vite + TypeScript, en una carpeta independiente `frontend/`, que permita ejecutar visualmente el flujo completo de órdenes de OrderApi (crear, liberar, decidir vía Signal o Update, consultar estado y ver el reporte) para observar en vivo los dos escenarios del README (Signal aprobada / Signal rechazada).

## Alcance

**Incluye:**

- App React + Vite + TypeScript en `frontend/` (carpeta nueva, independiente del `.sln`).
- Cliente HTTP hacia OrderApi con URL base configurable por variable de entorno (`VITE_API_URL`, default `http://localhost:5000`).
- Pantallas/flujos que cubren los endpoints existentes de OrderApi:
  - Crear orden (`POST /orders`).
  - Listar órdenes (`GET /orders`).
  - Liberar orden / iniciar `ReleaseOrderWorkflow` (`POST /orders/{orderId}/release`).
  - Enviar decisión de release, con selector explícito **Signal** vs **Update**:
    - Signal → `POST /orders/{orderId}/release/decision`.
    - Update → `POST /orders/{orderId}/release/decision-update`.
  - Ver estado del workflow con polling automático (`GET /orders/{orderId}/status`), deteniendo el polling al llegar a un estado terminal (`Completed`, `Compensated`, `CompensationFailed`, `Failed`).
  - Ver reporte de una orden (`GET /reports/{orderId}`).
- Manejo visible de estados de error: orden/workflow no encontrado (404), error de validación del Update (400), error de red/API caído.
- Cambio mínimo en backend: habilitar CORS en `OrderApi` (`src/OrderApi/Program.cs`) para permitir que el frontend, corriendo en otro origen (ej. `http://localhost:5173`), pueda invocar la API.
- CSS plano (sin librería de componentes) para mantener el estilo didáctico del repo.

**No incluye (fuera de alcance de este spec):**

- Autenticación o autorización.
- Persistencia en el navegador (localStorage, etc.) — todo se pierde al recargar, la lista de órdenes se relee de `GET /orders` en cada carga.
- Integración con `docker-compose.yml` (no se agrega un servicio `frontend`; corre standalone con `npm run dev`). Puede evaluarse en un spec futuro.
- Tests automatizados de frontend (no hay suite de tests en el resto del repo).
- Websockets o Server-Sent Events para status en tiempo real — se usa polling simple.
- Cambios al esquema de datos o a los workflows/activities de Temporal.

## Modelo de datos

No se introducen estructuras de datos nuevas en el backend. El frontend consume y tipa (en TypeScript) los DTOs ya expuestos por `Contracts.Dtos` y usados por `OrderApi`:

```ts
// tipos TS espejados de Contracts/Dtos/OrderDto.cs e IReleaseOrderWorkFlow.cs
interface OrderDto {
  orderId: number;
  orderCode: string;
  productId: number;
  quantity: number;
  amount: number;
  status: string;
  createdAt: string;
  updatedAt?: string | null;
  address: string;
}

interface CreateOrderRequest {
  orderCode: string;
  quantity: number;
  productId: number;
  amount: number;
  address: string;
}

interface ReleaseDecision {
  approved: boolean;
  reason?: string | null;
}

interface OrderStatusResponse {
  workflowId: string;
  status: string; // GetStatus() del workflow (paso actual)
  state: string; // estado de ejecución de Temporal (Running/Completed/...)
}

interface OrderReportResult {
  orderId: number;
  status: string;
  generatedAt: string;
  summary: string;
}
```

Estructura de carpetas propuesta dentro de `frontend/`:

```
frontend/
  index.html
  package.json
  vite.config.ts
  tsconfig.json
  .env.example
  src/
    main.tsx
    App.tsx
    api/
      client.ts        # fetch wrapper + VITE_API_URL
      orders.ts         # funciones tipadas por endpoint
    types/
      dtos.ts            # interfaces de arriba
    components/
      OrderList.tsx
      CreateOrderForm.tsx
      ReleaseOrderPanel.tsx   # release + selector Signal/Update + status polling
      OrderReport.tsx
      ErrorBanner.tsx
```

## Plan de implementación

1. **Scaffold del proyecto.** Crear `frontend/` con `npm create vite@latest -- --template react-ts`, limpiar boilerplate por defecto. Agregar `.env.example` con `VITE_API_URL=http://localhost:5000` y `.gitignore` de Vite (node_modules, dist).
2. **Habilitar CORS en OrderApi.** En `src/OrderApi/Program.cs`, agregar `builder.Services.AddCors(...)` con una policy que permita el origen del dev server de Vite (configurable, ej. `http://localhost:5173`) y `app.UseCors(...)` antes de los `MapGet/MapPost`. Verificar que Swagger sigue funcionando.
3. **Cliente API tipado.** Implementar `src/api/client.ts` (fetch wrapper que lee `VITE_API_URL`, maneja JSON y errores HTTP) y `src/api/orders.ts` con una función por endpoint, usando los tipos de `src/types/dtos.ts`.
4. **Listado y creación de órdenes.** `OrderList.tsx` (tabla con `GET /orders`, refresco manual) y `CreateOrderForm.tsx` (formulario que llama `POST /orders` y refresca la lista). Validación mínima de campos requeridos.
5. **Panel de liberación y decisión.** `ReleaseOrderPanel.tsx`: botón "Liberar orden" (`POST /orders/{id}/release`), selector Signal/Update, formulario de decisión (`approved`, `reason`) que llama al endpoint correspondiente según el selector.
6. **Polling de estado.** Dentro de `ReleaseOrderPanel.tsx` (o un hook `useOrderStatus`), polling cada 2-3s a `GET /orders/{id}/status` mientras el estado no sea terminal (`Completed`, `Compensated`, `CompensationFailed`, `Failed`); mostrar el paso actual (`status`) y el estado de ejecución (`state`).
7. **Manejo de errores.** `ErrorBanner.tsx` reutilizable para mostrar 404 (orden/workflow no encontrado), 400 (Update rechazado por el validador) y errores de red, sin romper el resto de la UI.
8. **Vista de reporte.** `OrderReport.tsx`: botón "Ver reporte" que llama `GET /reports/{id}` y muestra el resultado (`status`, `generatedAt`, `summary`).
9. **Ensamblado en `App.tsx`.** Layout simple: lista de órdenes a la izquierda/arriba, panel de detalle (release/decisión/status/reporte) para la orden seleccionada.
10. **Verificación manual end-to-end.** Con el stack completo corriendo (`docker compose up`) y el frontend en `npm run dev`, ejecutar los dos escenarios del README (Signal aprobada, Signal rechazada) y repetirlos usando Update en vez de Signal.

## Criterios de aceptación

- [x] `frontend/` existe en la raíz del repo con una app Vite + React + TypeScript funcional (`npm install && npm run dev` levanta el dev server sin errores).
- [x] La URL de OrderApi es configurable vía `VITE_API_URL` (variable de entorno / `.env`), con `.env.example` documentado.
- [x] `OrderApi` acepta requests CORS desde el origen del dev server de Vite sin romper Swagger ni el resto de endpoints.
- [x] Se puede crear una orden desde la UI y verla aparecer en el listado (`GET /orders`).
- [x] Se puede liberar una orden desde la UI (`POST /orders/{id}/release`) y ver su estado cambiar vía polling automático.
- [x] Se puede enviar una decisión de release tanto por Signal como por Update, eligiendo el mecanismo desde un selector en la UI, y el resultado (aprobado/rechazado) se refleja en el estado consultado.
- [x] El polling de estado se detiene automáticamente al llegar a un estado terminal (`Completed`, `Compensated`, `CompensationFailed`, `Failed`).
- [x] Consultar un `orderId` inexistente o un workflow no iniciado muestra un mensaje de error legible en la UI, sin romper la pantalla.
- [x] Se puede ver el reporte de una orden (`GET /reports/{orderId}`) desde la UI.
- [x] Se reproducen manualmente, desde la UI, los dos escenarios de prueba descritos en el README (Signal aprobada y Signal rechazada).

## Decisiones tomadas y descartadas

- **TypeScript + CSS plano, sin librería de componentes:** se prioriza mantener el espíritu didáctico y simple del repo (igual que el backend, sin frameworks extra que agreguen ruido conceptual). Se descartó Tailwind para no sumar configuración adicional a un demo.
- **URL de API por variable de entorno, sin integrar a docker-compose:** permite iterar rápido con `npm run dev` sin rebuild de imágenes; la integración a compose queda como posible spec futuro si se necesita un demo "todo en un `docker compose up`".
- **Selector explícito Signal/Update:** refuerza el propósito didáctico del repo (comparar ambos mecanismos), en vez de fijar uno solo y ocultar el otro.
- **Sin persistencia en el navegador:** consistente con que el repo no tiene estado de UI persistente en ningún otro lado; la lista de órdenes siempre se relee del backend.
- **Polling en vez de WebSockets/SSE:** el backend no expone streaming de eventos; agregar eso sería un cambio de infraestructura fuera del alcance de un frontend de demo.
- **CORS habilitado con policy explícita (no `AllowAnyOrigin` sin restricción) en Program.cs:** cambio mínimo y aislado, no afecta otros clientes de la API (Swagger, Postman, etc.).

## Riesgos identificados

- **CORS mal configurado** puede bloquear silenciosamente las llamadas del frontend; se mitiga verificando explícitamente en el paso 2 del plan que las requests desde el dev server de Vite funcionan antes de seguir.
- **Polling agresivo** contra `GET /orders/{id}/status` podría generar carga innecesaria sobre Temporal si el intervalo es muy corto o no se detiene en estados terminales; el plan fija un intervalo de 2-3s y corte automático al llegar a estado terminal.
- **Desync entre el `status` del workflow (`GetStatus()`) y el `Status` de la tabla `Orders`:** ambos son fuentes de estado paralelas (documentado en CLAUDE.md); la UI debe dejar claro que "Status" viene del workflow, no de la orden en BD, para no confundir al usuario del demo.
