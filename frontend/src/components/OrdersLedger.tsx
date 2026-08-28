import type { OrderDto } from '../types/dtos'
import { ApiError } from '../api/client'
import { productName } from '../catalog'
import { formatAmount } from '../lib/format'
import { Card } from './ui/Card'
import { StatusBadge } from './ui/StatusBadge'
import { ErrorBanner } from './ErrorBanner'

interface OrdersLedgerProps {
  orders: OrderDto[]
  loading: boolean
  error: ApiError | null
  selectedOrderId: number | null
  onSelect: (orderId: number) => void
}

export function OrdersLedger({
  orders,
  loading,
  error,
  selectedOrderId,
  onSelect,
}: OrdersLedgerProps) {
  const sorted = [...orders].sort((a, b) => b.orderId - a.orderId)

  return (
    <Card>
      <div className="flex items-baseline justify-between">
        <h2 className="font-mono text-[0.6875rem] font-semibold uppercase tracking-[0.2em] text-thermal">
          Mis pedidos
        </h2>
        <span className="font-mono text-[0.6875rem] text-thermal">
          {orders.length === 0 ? '—' : `${orders.length} en total`}
        </span>
      </div>

      <ErrorBanner error={error} />

      {loading && orders.length === 0 && (
        <p className="mt-3 font-mono text-[0.8125rem] text-thermal">cargando pedidos…</p>
      )}

      {!loading && orders.length === 0 && !error && (
        <p className="mt-3 font-mono text-[0.8125rem] text-thermal">
          Todavía no hay pedidos. Armá uno en el mostrador.
        </p>
      )}

      <ul className="mt-3 -mx-1 max-h-[22rem] space-y-2 overflow-y-auto px-1">
        {sorted.map((order) => {
          const selected = order.orderId === selectedOrderId
          return (
            <li key={order.orderId}>
              <button
                type="button"
                aria-current={selected}
                onClick={() => onSelect(order.orderId)}
                className={`group flex w-full cursor-pointer items-center gap-2 rounded-sm bg-paper px-3 py-2.5 text-left text-ink ring-1 ring-inset transition duration-150 ${
                  selected
                    ? 'ring-2 ring-ledger-bright'
                    : 'ring-rule hover:-translate-y-0.5 hover:ring-ink-soft hover:shadow-[0_8px_20px_-10px_rgba(0,0,0,0.65)]'
                }`}
              >
                <div className="min-w-0 flex-1">
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-mono text-sm font-medium">{order.orderCode}</span>
                    <StatusBadge status={order.status} />
                  </div>
                  <div className="mt-1 flex items-center justify-between gap-2 font-mono text-[0.6875rem] text-ink-soft">
                    <span className="truncate">
                      {order.quantity}× {productName(order.productId)}
                    </span>
                    <span className="tabular-nums">{formatAmount(order.amount)}</span>
                  </div>
                </div>
                <span
                  aria-hidden
                  className={`shrink-0 font-mono text-base transition ${
                    selected
                      ? 'text-ledger-bright'
                      : 'text-rule group-hover:translate-x-0.5 group-hover:text-ink-soft'
                  }`}
                >
                  ›
                </span>
              </button>
            </li>
          )
        })}
      </ul>
    </Card>
  )
}
