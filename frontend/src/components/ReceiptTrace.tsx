import type { TraceLine } from '../hooks/useReceiptTrace'
import { formatClock } from '../lib/format'
import { stepGlyph, stepMeta } from '../lib/workflowSteps'
import type { TraceKind } from '../lib/workflowSteps'

interface ReceiptTraceProps {
  lines: TraceLine[]
  compensated: boolean
}

const KIND_TEXT: Record<TraceKind, string> = {
  done: 'text-ink',
  wait: 'text-[#946a1e]',
  ok: 'text-ledger font-semibold',
  bad: 'text-inkred',
  reversed: 'text-inkred',
}

const KIND_GLYPH: Record<TraceKind, string> = {
  done: 'text-ledger',
  wait: 'text-[#946a1e]',
  ok: 'text-ledger',
  bad: 'text-inkred',
  reversed: 'text-inkred',
}

export function ReceiptTrace({ lines, compensated }: ReceiptTraceProps) {
  return (
    <div className="relative">
      <ol className="space-y-1.5">
        {lines.map((line, index) => {
          const meta = stepMeta(line.step)
          // Un paso "en espera" que ya no es el último quedó resuelto.
          const kind: TraceKind =
            meta.kind === 'wait' && index < lines.length - 1 ? 'done' : meta.kind
          return (
            <li
              key={line.step}
              className="trace-line flex items-baseline gap-3 font-mono text-[0.8125rem]"
            >
              <span className="tabular-nums text-ink-soft">
                {line.at ? formatClock(line.at) : '  ·  ·  '}
              </span>
              <span
                className={`w-6 shrink-0 ${KIND_GLYPH[kind]} ${
                  kind === 'wait' ? 'trace-wait' : ''
                }`}
              >
                {stepGlyph(kind)}
              </span>
              <span className={KIND_TEXT[kind]}>{meta.label}</span>
            </li>
          )
        })}
      </ol>

      {compensated && (
        <span
          aria-hidden
          className="stamp-void pointer-events-none absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 whitespace-nowrap px-5 py-1.5 text-4xl"
        >
          anulado
        </span>
      )}
    </div>
  )
}
