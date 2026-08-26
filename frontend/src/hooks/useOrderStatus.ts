import { useEffect, useRef, useState } from 'react'
import { getOrderStatus } from '../api/orders'
import { ApiError } from '../api/client'
import type { OrderStatusResponse } from '../types/dtos'

const TERMINAL_STATES = ['Completed', 'Compensated', 'CompensationFailed', 'Failed']
const POLL_INTERVAL_MS = 2500

export function useOrderStatus(orderId: number, active: boolean) {
  const [status, setStatus] = useState<OrderStatusResponse | null>(null)
  const [error, setError] = useState<ApiError | null>(null)
  const [polling, setPolling] = useState(false)
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  function stop() {
    if (timerRef.current) {
      clearTimeout(timerRef.current)
      timerRef.current = null
    }
    setPolling(false)
  }

  async function tick() {
    try {
      const result = await getOrderStatus(orderId)
      setStatus(result)
      setError(null)
      if (TERMINAL_STATES.includes(result.status)) {
        stop()
        return
      }
    } catch (err) {
      setError(err instanceof ApiError ? err : new ApiError(0, 'Error inesperado', err))
      stop()
      return
    }
    timerRef.current = setTimeout(tick, POLL_INTERVAL_MS)
  }

  function start() {
    stop()
    setPolling(true)
    tick()
  }

  useEffect(() => {
    if (active) {
      start()
    } else {
      stop()
    }
    return stop
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [orderId, active])

  return { status, error, polling, restart: start }
}
