import { useEffect, useState } from 'react'
import { listOrders } from '../api/orders'
import { ApiError } from '../api/client'
import type { OrderDto } from '../types/dtos'

export function useOrders() {
  const [orders, setOrders] = useState<OrderDto[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)

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
  }, [])

  return { orders, loading, error, refresh }
}
