import type { OrderDto } from '../types/dtos'
import { useReceiptTrace } from '../hooks/useReceiptTrace'
import { productName } from '../catalog'
import { formatAmount } from '../lib/format'
import { Card } from './ui/Card'
import { StatusBadge } from './ui/StatusBadge'
import { ErrorBanner } from './ErrorBanner'
import { ReceiptTrace } from './ReceiptTrace'
import { DecisionGate } from './DecisionGate'
import { ReceiptSummary } from './ReceiptSummary'

interface OrderReceiptProps {
  order: OrderDto
}

function Dashed() {
  return <div className="my-4 border-t border-dashed border-rule" />
}

export function OrderReceipt({ order }: OrderReceiptProps) {
  const { lines, status, error, notStarted, compensated, polling, restart } = useReceiptTrace(
    order.orderId,
    true,
  )

  const showError = error && error.status !== 404

  return (
    <Card
      variant="paper"
      className="receipt-edge rounded-sm px-6 py-7 shadow-[0_20px_44px_-26px_rgba(0,0,0,0.75)]"
    >
      <div className="text-center">
        <p className="font-display text-lg font-bold uppercase tracking-[0.22em] text-ink">
          La Placa
        </p>
        <p className="font-mono text-[0.625rem] uppercase tracking-[0.2em] text-ink-soft">
          comprobante de pedido
        </p>
      </div>

      <Dashed />

      <div className="flex items-center justify-between gap-3 font-mono text-sm">
        <span className="font-medium text-ink">
          {order.orderCode} <span className="text-ink-soft">#{order.orderId}</span>
        </span>
        <StatusBadge status={order.status} />
      </div>

      <div className="mt-3 flex items-baseline font-mono text-[0.8125rem] text-ink">
        <span>
          {order.quantity}× {productName(order.productId)}
        </span>
        <span className="leader" />
        <span className="tabular-nums">{formatAmount(order.amount)}</span>
      </div>
      <div className="mt-1 flex items-baseline font-mono text-[0.75rem] text-ink-soft">
        <span className="shrink-0">enviar a</span>
        <span className="leader" />
        <span className="text-right">{order.address}</span>
      </div>

      <Dashed />

      <p className="mb-2 font-mono text-[0.625rem] uppercase tracking-[0.2em] text-ink-soft">
        seguimiento
      </p>
      {notStarted ? (
        <p className="font-mono text-[0.8125rem] text-ink-soft">
          Sin enviar. El workflow todavía no arrancó.
        </p>
      ) : (
        <ReceiptTrace lines={lines} compensated={compensated} />
      )}
      {showError && <ErrorBanner error={error} onPaper />}

      <Dashed />

      <DecisionGate
        orderId={order.orderId}
        currentStep={status?.status}
        notStarted={notStarted}
        onAfterAction={restart}
      />

      <Dashed />

      <ReceiptSummary orderId={order.orderId} />

      <p className="mt-5 text-center font-mono text-[0.625rem] text-ink-soft">
        {status
          ? `${status.workflowId} · ${polling ? 'actualizando ●' : 'detenido'}`
          : 'sin workflow todavía'}
      </p>
    </Card>
  )
}
