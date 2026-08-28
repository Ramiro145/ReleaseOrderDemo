import { useCallback, useRef, useState } from 'react'
import { useOrderStatus } from './useOrderStatus'
import type { OrderStatusResponse } from '../types/dtos'
import { FORWARD_STEPS, isCompensationStep } from '../lib/workflowSteps'

export interface TraceLine {
  step: string
  at: Date | null // null = rellenado (el polling no lo vio pasar de a uno)
}

const FORWARD_INDEX = new Map<string, number>(FORWARD_STEPS.map((s, i) => [s, i]))

/**
 * Acumula los pasos que el workflow fue reportando en una lista ordenada, como
 * si se imprimieran en un ticket. `useOrderStatus` sólo devuelve el paso actual;
 * acá guardamos el histórico observado y rellenamos los pasos intermedios de la
 * secuencia feliz que el polling (cada 2,5 s) se haya saltado.
 *
 * Se espera montarse por pedido — App.tsx le pasa `key={orderId}` a OrderReceipt,
 * así que cada pedido arranca con su propia cinta.
 */
export function useReceiptTrace(orderId: number, active: boolean) {
  const [lines, setLines] = useState<TraceLine[]>([])
  const seenRef = useRef<Set<string>>(new Set())

  const handleResult = useCallback((result: OrderStatusResponse) => {
    const step = result.status
    if (seenRef.current.has(step)) return

    setLines((prev) => {
      const next = [...prev]
      const forwardPos = FORWARD_INDEX.get(step)

      if (forwardPos !== undefined) {
        for (let i = 0; i < forwardPos; i++) {
          const earlier = FORWARD_STEPS[i]
          if (!seenRef.current.has(earlier)) {
            seenRef.current.add(earlier)
            next.push({ step: earlier, at: null })
          }
        }
      }

      seenRef.current.add(step)
      next.push({ step, at: new Date() })
      return next
    })
  }, [])

  const { status, error, polling, restart } = useOrderStatus(orderId, active, handleResult)

  const notStarted = error?.status === 404 && lines.length === 0
  const compensated = lines.some((l) => isCompensationStep(l.step))

  return { lines, status, error, notStarted, compensated, polling, restart }
}
