import { useState } from 'react'
import { OrderList } from './components/OrderList'
import { CreateOrderForm } from './components/CreateOrderForm'
import { OrderDetailPanel } from './components/OrderDetailPanel'
import { Modal } from './components/ui/Modal'
import { useOrders } from './hooks/useOrders'

function App() {
  const { orders, loading, error, refresh } = useOrders()
  const [selectedOrderId, setSelectedOrderId] = useState<number | null>(null)
  const [isCreateOpen, setIsCreateOpen] = useState(false)

  const selectedOrder = orders.find((o) => o.orderId === selectedOrderId) ?? null

  function handleOrderCreated() {
    setIsCreateOpen(false)
    refresh()
  }

  return (
    <div className="min-h-screen bg-slate-50">
      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto max-w-6xl px-6 py-5">
          <h1 className="text-2xl font-semibold tracking-tight text-slate-900">
            ReleaseOrder Demo
          </h1>
          <p className="mt-1 text-sm text-slate-500">
            Temporal.io — SAGA, Signals y Updates sobre .NET 8
          </p>
        </div>
      </header>

      <main className="mx-auto max-w-6xl px-6 py-8">
        <div className="flex flex-col gap-6 lg:flex-row lg:items-start">
          <div className="flex min-w-0 flex-1 flex-col gap-6 lg:basis-[40%]">
            <OrderList
              orders={orders}
              loading={loading}
              error={error}
              onRefresh={refresh}
              onCreateOrder={() => setIsCreateOpen(true)}
              selectedOrderId={selectedOrderId}
              onSelectOrder={setSelectedOrderId}
            />
          </div>
          <div className="flex min-w-0 flex-1 flex-col gap-6 lg:basis-[60%]">
            {selectedOrder ? (
              <OrderDetailPanel order={selectedOrder} />
            ) : (
              <div className="rounded-lg border border-dashed border-slate-300 bg-white px-6 py-10 text-center text-sm text-slate-500">
                Seleccioná una orden de la lista para ver sus detalles.
              </div>
            )}
          </div>
        </div>
      </main>

      <Modal open={isCreateOpen} onClose={() => setIsCreateOpen(false)} title="Crear orden">
        <CreateOrderForm onOrderCreated={handleOrderCreated} />
      </Modal>
    </div>
  )
}

export default App
