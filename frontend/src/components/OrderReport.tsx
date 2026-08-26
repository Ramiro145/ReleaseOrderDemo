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
    <section>
      <h2>Reporte de la orden #{orderId}</h2>
      <button type="button" onClick={handleFetchReport} disabled={loading}>
        {loading ? 'Generando...' : 'Ver reporte'}
      </button>
      <ErrorBanner error={error} />
      {report && (
        <dl>
          <dt>Estado</dt>
          <dd>{report.status}</dd>
          <dt>Generado</dt>
          <dd>{report.generatedAt}</dd>
          <dt>Resumen</dt>
          <dd>{report.summary}</dd>
        </dl>
      )}
    </section>
  )
}
