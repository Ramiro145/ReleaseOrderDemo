import { useEffect, useState } from 'react'
import { listOrders } from '../api/orders'
import type { OrderDto } from '../types/dtos'
import { ApiError } from '../api/client'
import { ErrorBanner } from './ErrorBanner'

interface OrderListProps {
  refreshToken: number
  selectedOrderId: number | null
  onSelectOrder: (orderId: number) => void
}

const STATUS_STYLES: Record<string, string> = {
  Completed: 'bg-emerald-50 text-emerald-700 ring-1 ring-inset ring-emerald-200',
  Failed: 'bg-red-50 text-red-700 ring-1 ring-inset ring-red-200',
  Compensated: 'bg-amber-50 text-amber-700 ring-1 ring-inset ring-amber-200',
  CompensationFailed: 'bg-red-50 text-red-700 ring-1 ring-inset ring-red-200',
}

const DEFAULT_STATUS_STYLE = 'bg-slate-100 text-slate-600 ring-1 ring-inset ring-slate-200'

function StatusBadge({ status }: { status: string }) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${
        STATUS_STYLES[status] ?? DEFAULT_STATUS_STYLE
      }`}
    >
      {status}
    </span>
  )
}

export function OrderList({ refreshToken, selectedOrderId, onSelectOrder }: OrderListProps) {
  const [orders, setOrders] = useState<OrderDto[]>([])
  const [error, setError] = useState<ApiError | null>(null)
  const [loading, setLoading] = useState(false)

  async function refresh() {
    setLoading(true)
    setError(null)
    try {
      const result = await listOrders()
      setOrders(result)
    } catch (err) {
      setError(err instanceof ApiError ? err : new ApiError(0, 'Error inesperado', err))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    refresh()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [refreshToken])

  return (
    <section className="rounded-lg border border-slate-200 bg-white p-5 shadow-sm">
      <div className="flex items-center justify-between gap-3">
        <h2 className="text-lg font-semibold text-slate-900">Órdenes</h2>
        <button
          type="button"
          onClick={refresh}
          disabled={loading}
          className="rounded-md bg-slate-900 px-3 py-1.5 text-sm font-medium text-white transition hover:bg-slate-700 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {loading ? 'Actualizando...' : 'Refrescar'}
        </button>
      </div>

      <ErrorBanner error={error} />

      <div className="mt-3 overflow-x-auto rounded-md border border-slate-200">
        <table className="w-full min-w-[640px] table-auto border-collapse text-sm">
          <thead>
            <tr className="bg-slate-50 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
              <th className="px-3 py-2">Id</th>
              <th className="px-3 py-2">Código</th>
              <th className="px-3 py-2">Producto</th>
              <th className="px-3 py-2">Cantidad</th>
              <th className="px-3 py-2">Monto</th>
              <th className="px-3 py-2">Estado</th>
              <th className="px-3 py-2">Dirección</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {orders.map((order) => {
              const isSelected = order.orderId === selectedOrderId
              return (
                <tr
                  key={order.orderId}
                  onClick={() => onSelectOrder(order.orderId)}
                  className={`cursor-pointer transition ${
                    isSelected ? 'bg-indigo-50' : 'hover:bg-slate-50'
                  }`}
                >
                  <td className="px-3 py-2 text-slate-700">{order.orderId}</td>
                  <td className="px-3 py-2 font-medium text-slate-900">{order.orderCode}</td>
                  <td className="px-3 py-2 text-slate-700">{order.productId}</td>
                  <td className="px-3 py-2 text-slate-700">{order.quantity}</td>
                  <td className="px-3 py-2 text-slate-700">{order.amount}</td>
                  <td className="px-3 py-2">
                    <StatusBadge status={order.status} />
                  </td>
                  <td className="px-3 py-2 text-slate-700">{order.address}</td>
                </tr>
              )
            })}
            {orders.length === 0 && !loading && (
              <tr>
                <td colSpan={7} className="px-3 py-6 text-center text-slate-400">
                  No hay órdenes para mostrar.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </section>
  )
}
