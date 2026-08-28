import { useState } from 'react'
import {
  releaseOrder,
  sendReleaseDecisionSignal,
  sendReleaseDecisionUpdate,
} from '../api/orders'
import { ApiError } from '../api/client'
import { isTerminalStep, isWaitingStep } from '../lib/workflowSteps'
import { ErrorBanner } from './ErrorBanner'

type Mechanism = 'signal' | 'update'
type Pending = 'release' | 'confirm' | 'reject' | null

interface DecisionGateProps {
  orderId: number
  currentStep: string | undefined
  notStarted: boolean
  onAfterAction: () => void
}

const paperInput =
  'mt-1 w-full rounded-md border border-ink/20 bg-white px-3 py-1.5 text-sm text-ink outline-none focus:border-ledger'

export function DecisionGate({
  orderId,
  currentStep,
  notStarted,
  onAfterAction,
}: DecisionGateProps) {
  const [mechanism, setMechanism] = useState<Mechanism>('signal')
  const [reason, setReason] = useState('')
  const [pending, setPending] = useState<Pending>(null)
  const [error, setError] = useState<ApiError | null>(null)
  const [info, setInfo] = useState<string | null>(null)

  const started = !notStarted && currentStep !== undefined
  const closed = currentStep !== undefined && isTerminalStep(currentStep)
  const waiting = currentStep !== undefined && isWaitingStep(currentStep)

  async function handleRelease() {
    setError(null)
    setInfo(null)
    setPending('release')
    try {
      const result = await releaseOrder(orderId)
      setInfo(`Workflow en marcha (${result.workflowId}).`)
      onAfterAction()
    } catch (err) {
      setError(err instanceof ApiError ? err : new ApiError(0, 'Error inesperado', err))
    } finally {
      setPending(null)
    }
  }

  async function handleDecision(approved: boolean) {
    setError(null)
    setInfo(null)
    setPending(approved ? 'confirm' : 'reject')
    try {
      const decision = { approved, reason: reason.trim() || null }
      if (mechanism === 'signal') {
        await sendReleaseDecisionSignal(orderId, decision)
        setInfo(approved ? 'Confirmado por Signal.' : 'Rechazado por Signal.')
      } else {
        const result = await sendReleaseDecisionUpdate(orderId, decision)
        setInfo(`Update aceptado: ${result.result}`)
      }
      onAfterAction()
    } catch (err) {
      setError(err instanceof ApiError ? err : new ApiError(0, 'Error inesperado', err))
    } finally {
      setPending(null)
    }
  }

  if (closed) return null

  return (
    <div className="border-2 border-double border-ink/25 p-4">
      {!started && (
        <>
          <p className="font-mono text-[0.8125rem] leading-relaxed text-ink-soft">
            Este pedido todavía no entró a preparación. Al enviarlo arranca el
            workflow durable.
          </p>
          <button
            type="button"
            onClick={handleRelease}
            disabled={pending !== null}
            className="mt-3 inline-flex cursor-pointer items-center rounded-md bg-ledger px-4 py-2 text-sm font-medium text-paper transition hover:bg-ledger-bright disabled:cursor-not-allowed disabled:opacity-45"
          >
            {pending === 'release' ? 'enviando…' : 'Enviar a preparación'}
          </button>
        </>
      )}

      {started && (
        <>
          <h3 className="font-display text-base font-bold text-ink">
            ¿Confirmás este pedido?
          </h3>
          {!waiting && (
            <p className="mt-1 font-mono text-[0.75rem] leading-relaxed text-ink-soft">
              El workflow todavía no llegó a la espera. Un Signal queda encolado;
              un Update devuelve 400 hasta que esté listo.
            </p>
          )}

          <fieldset className="mt-3 flex flex-col gap-1.5">
            <legend className="sr-only">Mecanismo</legend>
            {(
              [
                ['signal', 'Signal', 'se encola y no responde; el workflow lo toma cuando puede'],
                ['update', 'Update', 'espera la respuesta del workflow; devuelve 400 si aún no está listo'],
              ] as const
            ).map(([value, title, hint]) => (
              <label key={value} className="flex cursor-pointer items-start gap-2 text-sm text-ink">
                <input
                  type="radio"
                  name="mechanism"
                  className="mt-1 accent-ledger"
                  checked={mechanism === value}
                  onChange={() => setMechanism(value)}
                />
                <span>
                  <span className="font-medium">{title}</span>
                  <span className="block font-mono text-[0.6875rem] leading-snug text-ink-soft">
                    {hint}
                  </span>
                </span>
              </label>
            ))}
          </fieldset>

          <label className="mt-3 block text-sm font-medium text-ink">
            Motivo (opcional)
            <input
              className={paperInput}
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder="queda escrito en el ticket"
            />
          </label>

          <div className="mt-4 flex flex-wrap gap-2">
            <button
              type="button"
              onClick={() => handleDecision(true)}
              disabled={pending !== null}
              className="inline-flex cursor-pointer items-center rounded-md bg-ledger px-4 py-2 text-sm font-medium text-paper transition hover:bg-ledger-bright disabled:cursor-not-allowed disabled:opacity-45"
            >
              {pending === 'confirm' ? 'enviando…' : 'Confirmar pedido'}
            </button>
            <button
              type="button"
              onClick={() => handleDecision(false)}
              disabled={pending !== null}
              className="inline-flex cursor-pointer items-center rounded-md border-2 border-inkred px-4 py-2 text-sm font-medium text-inkred transition hover:bg-inkred/8 disabled:cursor-not-allowed disabled:opacity-45"
            >
              {pending === 'reject' ? 'enviando…' : 'Rechazar'}
            </button>
          </div>
        </>
      )}

      <ErrorBanner error={error} onPaper />
      {info && <p className="mt-2 font-mono text-[0.75rem] text-ledger">{info}</p>}
    </div>
  )
}
