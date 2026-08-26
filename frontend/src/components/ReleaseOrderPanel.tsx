import { useState } from 'react'
import type { FormEvent } from 'react'
import {
  releaseOrder,
  sendReleaseDecisionSignal,
  sendReleaseDecisionUpdate,
} from '../api/orders'
import { ApiError } from '../api/client'
import { ErrorBanner } from './ErrorBanner'
import { Button } from './ui/Button'

type DecisionMechanism = 'signal' | 'update'

interface ReleaseOrderPanelProps {
  orderId: number
  onAfterAction: () => void
}

const inputClass =
  'mt-1 w-full rounded-md border border-slate-300 px-3 py-1.5 text-sm text-slate-900 shadow-sm outline-none transition focus:border-indigo-400 focus:ring-2 focus:ring-indigo-100'

export function ReleaseOrderPanel({ orderId, onAfterAction }: ReleaseOrderPanelProps) {
  const [releaseError, setReleaseError] = useState<ApiError | null>(null)
  const [releasing, setReleasing] = useState(false)
  const [releaseInfo, setReleaseInfo] = useState<string | null>(null)

  const [mechanism, setMechanism] = useState<DecisionMechanism>('signal')
  const [approved, setApproved] = useState(true)
  const [reason, setReason] = useState('')
  const [decisionError, setDecisionError] = useState<ApiError | null>(null)
  const [decisionInfo, setDecisionInfo] = useState<string | null>(null)
  const [sendingDecision, setSendingDecision] = useState(false)

  async function handleRelease() {
    setReleaseError(null)
    setReleaseInfo(null)
    setReleasing(true)
    try {
      const result = await releaseOrder(orderId)
      setReleaseInfo(`Workflow iniciado: ${result.workflowId}`)
      onAfterAction()
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
      onAfterAction()
    } catch (err) {
      setDecisionError(err instanceof ApiError ? err : new ApiError(0, 'Error inesperado', err))
    } finally {
      setSendingDecision(false)
    }
  }

  return (
    <div>
      <ErrorBanner error={releaseError} />
      {releaseInfo && <p className="mt-2 text-sm text-emerald-700">{releaseInfo}</p>}
      <Button onClick={handleRelease} loading={releasing} loadingLabel="Liberando...">
        Liberar orden
      </Button>

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
        <Button
          type="submit"
          variant="primary"
          loading={sendingDecision}
          loadingLabel="Enviando..."
          className="w-fit"
        >
          Enviar decisión ({mechanism === 'signal' ? 'Signal' : 'Update'})
        </Button>
      </form>
    </div>
  )
}
