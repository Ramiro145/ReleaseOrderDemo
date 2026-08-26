import { useState } from 'react'
import { getOrderReport } from '../api/orders'
import type { OrderReportResult } from '../types/dtos'
import { ApiError } from '../api/client'
import { ErrorBanner } from './ErrorBanner'
import { Button } from './ui/Button'

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
    <div>
      <Button onClick={handleFetchReport} loading={loading} loadingLabel="Generando...">
        Ver reporte
      </Button>
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
    </div>
  )
}
