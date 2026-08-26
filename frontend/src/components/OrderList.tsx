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
    <section>
      <h2>Órdenes</h2>
      <button type="button" onClick={refresh} disabled={loading}>
        {loading ? 'Actualizando...' : 'Refrescar'}
      </button>
      <ErrorBanner error={error} />
      <table border={1} cellPadding={4} style={{ borderCollapse: 'collapse', width: '100%', marginTop: 8 }}>
        <thead>
          <tr>
            <th>Id</th>
            <th>Código</th>
            <th>Producto</th>
            <th>Cantidad</th>
            <th>Monto</th>
            <th>Estado</th>
            <th>Dirección</th>
          </tr>
        </thead>
        <tbody>
          {orders.map((order) => (
            <tr
              key={order.orderId}
              onClick={() => onSelectOrder(order.orderId)}
              style={{
                cursor: 'pointer',
                background: order.orderId === selectedOrderId ? '#eef' : undefined,
              }}
            >
              <td>{order.orderId}</td>
              <td>{order.orderCode}</td>
              <td>{order.productId}</td>
              <td>{order.quantity}</td>
              <td>{order.amount}</td>
              <td>{order.status}</td>
              <td>{order.address}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  )
}
