import { useState } from 'react'
import { Header } from './components/Header'
import { Counter } from './components/Counter'
import { OrdersLedger } from './components/OrdersLedger'
import { OrderReceipt } from './components/OrderReceipt'
import { Card } from './components/ui/Card'
import { useOrders } from './hooks/useOrders'

function EmptyReceipt() {
  return (
    <Card variant="paper" className="receipt-edge rounded-sm px-6 py-16 text-center">
      <p className="font-display text-lg font-bold uppercase tracking-[0.22em] text-ink">
        La Placa
      </p>
      <p className="mt-3 font-mono text-[0.8125rem] leading-relaxed text-ink-soft">
        Elegí un pedido de la lista
        <br />o armá uno nuevo en el mostrador.
      </p>
    </Card>
  )
}

function App() {
  const { orders, loading, error, refresh } = useOrders()
  const [selectedOrderId, setSelectedOrderId] = useState<number | null>(null)

  const selectedOrder = orders.find((o) => o.orderId === selectedOrderId) ?? null
  const connected = !(error && error.status === 0)

  function handleOrderPlaced(orderId: number) {
    refresh()
    setSelectedOrderId(orderId)
  }

  return (
    <div className="min-h-screen bg-counter">
      <Header connected={connected} onRefresh={refresh} refreshing={loading} />

      <main className="mx-auto max-w-6xl px-4 py-8 sm:px-6">
        <div className="grid gap-6 lg:grid-cols-[minmax(0,360px)_minmax(0,1fr)] lg:items-start">
          <div className="flex flex-col gap-6">
            <Counter onOrderPlaced={handleOrderPlaced} />
            <OrdersLedger
              orders={orders}
              loading={loading}
              error={error}
              selectedOrderId={selectedOrderId}
              onSelect={setSelectedOrderId}
            />
          </div>

          <div>
            {selectedOrder ? (
              <OrderReceipt key={selectedOrder.orderId} order={selectedOrder} />
            ) : (
              <EmptyReceipt />
            )}
          </div>
        </div>
      </main>
    </div>
  )
}

export default App
