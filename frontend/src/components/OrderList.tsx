import { useState } from 'react'
import type { OrderDto } from '../types/dtos'
import { ApiError } from '../api/client'
import { ErrorBanner } from './ErrorBanner'
import { Card } from './ui/Card'
import { Button } from './ui/Button'
import { StatusBadge } from './ui/StatusBadge'
import { Pagination } from './ui/Pagination'

interface OrderListProps {
  orders: OrderDto[]
  loading: boolean
  error: ApiError | null
  onRefresh: () => void
  onCreateOrder: () => void
  selectedOrderId: number | null
  onSelectOrder: (orderId: number) => void
}

const amountFormatter = new Intl.NumberFormat('es-AR', { style: 'currency', currency: 'ARS' })

export function OrderList({
  orders,
  loading,
  error,
  onRefresh,
  onCreateOrder,
  selectedOrderId,
  onSelectOrder,
}: OrderListProps) {
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)

  const totalPages = Math.max(1, Math.ceil(orders.length / pageSize))
  // Si se refrescó y hay menos órdenes/páginas que antes, mostramos la última página válida
  // en vez de dejar la tabla en blanco, sin necesidad de un efecto adicional.
  const currentPage = Math.min(page, totalPages)

  const pageOrders = orders.slice((currentPage - 1) * pageSize, currentPage * pageSize)

  function handlePageSizeChange(size: number) {
    setPageSize(size)
    setPage(1)
  }

  function selectRow(orderId: number) {
    onSelectOrder(orderId)
  }

  return (
    <Card>
      <div className="flex items-center justify-between gap-3">
        <h2 className="text-lg font-semibold text-slate-900">Órdenes</h2>
        <div className="flex gap-2">
          <Button variant="ghost" onClick={onRefresh} loading={loading} loadingLabel="Actualizando...">
            Refrescar
          </Button>
          <Button variant="primary" onClick={onCreateOrder}>
            + Nueva orden
          </Button>
        </div>
      </div>

      <ErrorBanner error={error} />

      <div className="mt-3 overflow-x-auto rounded-md border border-slate-200">
        <table className="w-full table-auto border-collapse text-sm">
          <thead>
            <tr className="bg-slate-50 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
              <th scope="col" className="px-3 py-2">Id</th>
              <th scope="col" className="px-3 py-2">Código</th>
              <th scope="col" className="px-3 py-2">Monto</th>
              <th scope="col" className="px-3 py-2">Estado</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {pageOrders.map((order) => {
              const isSelected = order.orderId === selectedOrderId
              return (
                <tr
                  key={order.orderId}
                  tabIndex={0}
                  aria-selected={isSelected}
                  onClick={() => selectRow(order.orderId)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault()
                      selectRow(order.orderId)
                    }
                  }}
                  className={`cursor-pointer outline-none transition focus-visible:ring-2 focus-visible:ring-indigo-400 ${
                    isSelected ? 'bg-indigo-50' : 'hover:bg-slate-50'
                  }`}
                >
                  <td className="px-3 py-2 text-slate-700">{order.orderId}</td>
                  <td className="px-3 py-2 font-medium text-slate-900">{order.orderCode}</td>
                  <td className="px-3 py-2 text-slate-700">{amountFormatter.format(order.amount)}</td>
                  <td className="px-3 py-2">
                    <StatusBadge status={order.status} />
                  </td>
                </tr>
              )
            })}
            {pageOrders.length === 0 && !loading && (
              <tr>
                <td colSpan={4} className="px-3 py-6 text-center text-slate-400">
                  No hay órdenes para mostrar.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      <Pagination
        page={currentPage}
        pageSize={pageSize}
        total={orders.length}
        onPageChange={setPage}
        onPageSizeChange={handlePageSizeChange}
      />
    </Card>
  )
}
