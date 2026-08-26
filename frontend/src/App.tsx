import { useState } from 'react'
import { OrderList } from './components/OrderList'
import { CreateOrderForm } from './components/CreateOrderForm'
import { ReleaseOrderPanel } from './components/ReleaseOrderPanel'
import { OrderReport } from './components/OrderReport'

function App() {
  const [refreshToken, setRefreshToken] = useState(0)
  const [selectedOrderId, setSelectedOrderId] = useState<number | null>(null)

  return (
    <main>
      <h1>ReleaseOrder Demo</h1>
      <div className="layout">
        <div className="layout-left">
          <CreateOrderForm onOrderCreated={() => setRefreshToken((t) => t + 1)} />
          <OrderList
            refreshToken={refreshToken}
            selectedOrderId={selectedOrderId}
            onSelectOrder={setSelectedOrderId}
          />
        </div>
        <div className="layout-right">
          {selectedOrderId !== null ? (
            <>
              <ReleaseOrderPanel orderId={selectedOrderId} />
              <OrderReport orderId={selectedOrderId} />
            </>
          ) : (
            <p>Seleccioná una orden de la lista para ver sus detalles.</p>
          )}
        </div>
      </div>
    </main>
  )
}

export default App
