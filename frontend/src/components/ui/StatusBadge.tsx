// Sello de goma estampado sobre el ticket. Traduce Orders.Status al
// vocabulario de la tienda.

interface StampSpec {
  label: string
  className: string
}

const NEUTRAL = 'text-ink-soft'
const GREEN = 'text-ledger'
const RED = 'text-inkred'
const AMBER = 'text-[#946a1e]' // ámbar legible sobre papel

const SPECS: Record<string, StampSpec> = {
  Created: { label: 'en mostrador', className: NEUTRAL },
  'Waiting for release decision': { label: 'espera tu ok', className: AMBER },
  Completed: { label: 'entregado', className: GREEN },
  Compensated: { label: 'anulado', className: RED },
  CompensationFailed: { label: 'reverso incompleto', className: RED },
  Failed: { label: 'fallido', className: RED },
}

export function StatusBadge({ status }: { status: string }) {
  const spec = SPECS[status] ?? { label: status.toLowerCase(), className: NEUTRAL }
  return (
    <span
      className={`stamp inline-flex rotate-[-2deg] items-center rounded-[3px] px-2 py-0.5 text-[0.6875rem] ${spec.className}`}
    >
      {spec.label}
    </span>
  )
}
