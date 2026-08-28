import { useState } from 'react'
import { getOrderReport } from '../api/orders'
import type { OrderReportResult } from '../types/dtos'
import { ApiError } from '../api/client'
import { ErrorBanner } from './ErrorBanner'

interface ReceiptSummaryProps {
  orderId: number
}

export function ReceiptSummary({ orderId }: ReceiptSummaryProps) {
  const [report, setReport] = useState<OrderReportResult | null>(null)
  const [error, setError] = useState<ApiError | null>(null)
  const [loading, setLoading] = useState(false)

  async function handleFetch() {
    setError(null)
    setLoading(true)
    try {
      const result = await getOrderReport(orderId)
      setReport(result.report)
    } catch (err) {
      setError(err instanceof ApiError ? err : new ApiError(0, 'Error inesperado', err))
      setReport(null)
    } finally {
      setLoading(false)
    }
  }

  return (
    <div>
      <button
        type="button"
        onClick={handleFetch}
        disabled={loading}
        className="inline-flex cursor-pointer items-center rounded-md px-3 py-1.5 text-sm font-medium text-ink ring-1 ring-inset ring-ink/20 transition hover:bg-ink/5 disabled:cursor-not-allowed disabled:opacity-45"
      >
        {loading ? 'generando…' : report ? 'actualizar comprobante' : 'Ver comprobante'}
      </button>

      <ErrorBanner error={error} onPaper />

      {report && (
        <dl className="mt-3 space-y-1 font-mono text-[0.75rem]">
          {(
            [
              ['estado', report.status],
              ['generado', report.generatedAt],
              ['resumen', report.summary],
            ] as const
          ).map(([term, value]) => (
            <div key={term} className="flex gap-2">
              <dt className="w-20 shrink-0 text-ink-soft">{term}</dt>
              <dd className="text-ink">{value}</dd>
            </div>
          ))}
        </dl>
      )}
    </div>
  )
}
