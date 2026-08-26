import { useState } from 'react'
import type { OrderDto } from '../types/dtos'
import { useOrderStatus } from '../hooks/useOrderStatus'
import { Card } from './ui/Card'
import { StatusBadge } from './ui/StatusBadge'
import { Tabs } from './ui/Tabs'
import { ErrorBanner } from './ErrorBanner'
import { ReleaseOrderPanel } from './ReleaseOrderPanel'
import { OrderReport } from './OrderReport'

interface OrderDetailPanelProps {
  order: OrderDto
}

export function OrderDetailPanel({ order }: OrderDetailPanelProps) {
  const [activeTab, setActiveTab] = useState('release')

  const { status, error: statusError, polling, restart: restartPolling } = useOrderStatus(order.orderId, true)

  return (
    <Card>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <h2 className="text-lg font-semibold text-slate-900">
            Orden {order.orderCode} <span className="font-normal text-slate-400">#{order.orderId}</span>
          </h2>
        </div>
        <StatusBadge status={order.status} />
      </div>

      <dl className="mt-3 grid grid-cols-2 gap-x-4 gap-y-1 text-sm sm:grid-cols-4">
        <dt className="text-slate-500">Producto</dt>
        <dd className="text-slate-800">{order.productId}</dd>
        <dt className="text-slate-500">Cantidad</dt>
        <dd className="text-slate-800">{order.quantity}</dd>
        <dt className="text-slate-500 sm:col-span-1">Dirección</dt>
        <dd className="text-slate-800 sm:col-span-3">{order.address}</dd>
      </dl>

      <div className="mt-4 flex items-center gap-2 rounded-md bg-slate-50 px-3 py-2 text-sm">
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

      <div className="mt-4">
        <Tabs
          activeId={activeTab}
          onChange={setActiveTab}
          items={[
            {
              id: 'release',
              label: 'Liberar',
              content: <ReleaseOrderPanel orderId={order.orderId} onAfterAction={restartPolling} />,
            },
            {
              id: 'report',
              label: 'Reporte',
              content: <OrderReport orderId={order.orderId} />,
            },
          ]}
        />
      </div>
    </Card>
  )
}
