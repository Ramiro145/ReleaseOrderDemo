import { useState } from 'react'
import { getOrderReport } from '../api/orders'
import type { OrderReportResult } from '../types/dtos'
import { ApiError } from '../api/client'
import { ErrorBanner } from './ErrorBanner'

interface OrderReportProps {
  orderId: number
}

export function OrderReport({ orderId }: OrderReportProps) {
  const [report, setReport] = useState<OrderReportResult | null>(null)
  const [error, setError] = useState<ApiError | null>(null)
  const [loading, setLoading] = useState(false)

  async function handleFetchReport() {
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
    <section className="rounded-lg border border-slate-200 bg-white p-5 shadow-sm">
      <h2 className="text-lg font-semibold text-slate-900">Reporte de la orden #{orderId}</h2>
      <button
        type="button"
        onClick={handleFetchReport}
        disabled={loading}
        className="mt-3 rounded-md bg-slate-900 px-4 py-1.5 text-sm font-medium text-white transition hover:bg-slate-700 disabled:cursor-not-allowed disabled:opacity-50"
      >
        {loading ? 'Generando...' : 'Ver reporte'}
      </button>
      <ErrorBanner error={error} />
      {report && (
        <dl className="mt-4 grid grid-cols-1 gap-x-4 gap-y-2 text-sm sm:grid-cols-[auto_1fr]">
          <dt className="font-medium text-slate-500">Estado</dt>
          <dd className="text-slate-800">{report.status}</dd>
          <dt className="font-medium text-slate-500">Generado</dt>
          <dd className="text-slate-800">{report.generatedAt}</dd>
          <dt className="font-medium text-slate-500">Resumen</dt>
          <dd className="text-slate-800">{report.summary}</dd>
        </dl>
      )}
    </section>
  )
}
