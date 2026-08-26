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
    <section>
      <h2>Liberar orden #{orderId}</h2>

      <div>
        <strong>Estado del workflow:</strong>{' '}
        {status ? (
          <span>
            {status.status} ({status.state}) {polling ? '— actualizando...' : '— detenido'}
          </span>
        ) : (
          <span>sin datos aún</span>
        )}
      </div>
      <ErrorBanner error={statusError} />

      <ErrorBanner error={releaseError} />
      {releaseInfo && <p>{releaseInfo}</p>}
      <button type="button" onClick={handleRelease} disabled={releasing}>
        {releasing ? 'Liberando...' : 'Liberar orden'}
      </button>

      <h3>Decisión de release</h3>
      <ErrorBanner error={decisionError} />
      {decisionInfo && <p>{decisionInfo}</p>}
      <form onSubmit={handleDecisionSubmit}>
        <div>
          <label>
            <input
              type="radio"
              name="mechanism"
              value="signal"
              checked={mechanism === 'signal'}
              onChange={() => setMechanism('signal')}
            />
            Signal
          </label>
          <label style={{ marginLeft: 12 }}>
            <input
              type="radio"
              name="mechanism"
              value="update"
              checked={mechanism === 'update'}
              onChange={() => setMechanism('update')}
            />
            Update
          </label>
        </div>
        <div>
          <label>
            <input type="checkbox" checked={approved} onChange={(e) => setApproved(e.target.checked)} />
            Aprobado
          </label>
        </div>
        <div>
          <label>
            Motivo
            <input value={reason} onChange={(e) => setReason(e.target.value)} />
          </label>
        </div>
        <button type="submit" disabled={sendingDecision}>
          {sendingDecision ? 'Enviando...' : `Enviar decisión (${mechanism === 'signal' ? 'Signal' : 'Update'})`}
        </button>
      </form>
    </section>
  )
}
