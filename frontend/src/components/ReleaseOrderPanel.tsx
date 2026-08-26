import { useState } from 'react'
import type { FormEvent } from 'react'
import {
  releaseOrder,
  sendReleaseDecisionSignal,
  sendReleaseDecisionUpdate,
} from '../api/orders'
import { ApiError } from '../api/client'
import { ErrorBanner } from './ErrorBanner'
import { useOrderStatus } from '../hooks/useOrderStatus'

type DecisionMechanism = 'signal' | 'update'

interface ReleaseOrderPanelProps {
  orderId: number
}

const inputClass =
  'mt-1 w-full rounded-md border border-slate-300 px-3 py-1.5 text-sm text-slate-900 shadow-sm outline-none transition focus:border-indigo-400 focus:ring-2 focus:ring-indigo-100'

export function ReleaseOrderPanel({ orderId }: ReleaseOrderPanelProps) {
  const [releaseError, setReleaseError] = useState<ApiError | null>(null)
  const [releasing, setReleasing] = useState(false)
  const [releaseInfo, setReleaseInfo] = useState<string | null>(null)

  const [mechanism, setMechanism] = useState<DecisionMechanism>('signal')
  const [approved, setApproved] = useState(true)
  const [reason, setReason] = useState('')
  const [decisionError, setDecisionError] = useState<ApiError | null>(null)
  const [decisionInfo, setDecisionInfo] = useState<string | null>(null)
  const [sendingDecision, setSendingDecision] = useState(false)

  const { status, error: statusError, polling, restart: restartPolling } = useOrderStatus(orderId, true)

  async function handleRelease() {
    setReleaseError(null)
    setReleaseInfo(null)
    setReleasing(true)
    try {
      const result = await releaseOrder(orderId)
      setReleaseInfo(`Workflow iniciado: ${result.workflowId}`)
      restartPolling()
    } catch (err) {
      setReleaseError(err instanceof ApiError ? err : new ApiError(0, 'Error inesperado', err))
    } finally {
      setReleasing(false)
    }
  }

  async function handleDecisionSubmit(e: FormEvent) {
    e.preventDefault()
    setDecisionError(null)
    setDecisionInfo(null)
    setSendingDecision(true)
    try {
      const decision = { approved, reason: reason.trim() || null }
      if (mechanism === 'signal') {
        await sendReleaseDecisionSignal(orderId, decision)
        setDecisionInfo('Decisión enviada por Signal.')
      } else {
        const result = await sendReleaseDecisionUpdate(orderId, decision)
        setDecisionInfo(`Decisión enviada por Update. Resultado: ${result.result}`)
      }
      restartPolling()
    } catch (err) {
      setDecisionError(err instanceof ApiError ? err : new ApiError(0, 'Error inesperado', err))
    } finally {
      setSendingDecision(false)
    }
  }

  return (
    <section className="rounded-lg border border-slate-200 bg-white p-5 shadow-sm">
      <h2 className="text-lg font-semibold text-slate-900">Liberar orden #{orderId}</h2>

      <div className="mt-3 flex items-center gap-2 rounded-md bg-slate-50 px-3 py-2 text-sm">
        <span className="font-medium text-slate-700">Estado del workflow:</span>
        {status ? (
          <span className="text-slate-600">
            {status.status} ({status.state}){' '}
            <span className={polling ? 'text-emerald-600' : 'text-slate-400'}>
              {polling ? '— actualizando...' : '— detenido'}
            </span>
          </span>
        ) : (
          <span className="text-slate-400">sin datos aún</span>
        )}
      </div>
      <ErrorBanner error={statusError} />

      <ErrorBanner error={releaseError} />
      {releaseInfo && <p className="mt-2 text-sm text-emerald-700">{releaseInfo}</p>}
      <button
        type="button"
        onClick={handleRelease}
        disabled={releasing}
        className="mt-3 rounded-md bg-slate-900 px-4 py-1.5 text-sm font-medium text-white transition hover:bg-slate-700 disabled:cursor-not-allowed disabled:opacity-50"
      >
        {releasing ? 'Liberando...' : 'Liberar orden'}
      </button>

      <h3 className="mt-6 text-sm font-semibold uppercase tracking-wide text-slate-500">
        Decisión de release
      </h3>
      <ErrorBanner error={decisionError} />
      {decisionInfo && <p className="mt-2 text-sm text-emerald-700">{decisionInfo}</p>}
      <form onSubmit={handleDecisionSubmit} className="mt-3 flex flex-col gap-3">
        <div className="flex gap-4 text-sm text-slate-700">
          <label className="inline-flex items-center gap-1.5">
            <input
              type="radio"
              name="mechanism"
              value="signal"
              checked={mechanism === 'signal'}
              onChange={() => setMechanism('signal')}
              className="accent-indigo-600"
            />
            Signal
          </label>
          <label className="inline-flex items-center gap-1.5">
            <input
              type="radio"
              name="mechanism"
              value="update"
              checked={mechanism === 'update'}
              onChange={() => setMechanism('update')}
              className="accent-indigo-600"
            />
            Update
          </label>
        </div>
        <label className="inline-flex w-fit items-center gap-1.5 text-sm text-slate-700">
          <input
            type="checkbox"
            checked={approved}
            onChange={(e) => setApproved(e.target.checked)}
            className="accent-indigo-600"
          />
          Aprobado
        </label>
        <label className="block text-sm font-medium text-slate-700">
          Motivo
          <input className={inputClass} value={reason} onChange={(e) => setReason(e.target.value)} />
        </label>
        <button
          type="submit"
          disabled={sendingDecision}
          className="w-fit rounded-md bg-indigo-600 px-4 py-1.5 text-sm font-medium text-white transition hover:bg-indigo-500 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {sendingDecision ? 'Enviando...' : `Enviar decisión (${mechanism === 'signal' ? 'Signal' : 'Update'})`}
        </button>
      </form>
    </section>
  )
}
