import { useState } from 'react'
import { OrderList } from './components/OrderList'
import { CreateOrderForm } from './components/CreateOrderForm'
import { ReleaseOrderPanel } from './components/ReleaseOrderPanel'

function App() {
  const [refreshToken, setRefreshToken] = useState(0)
  const [selectedOrderId, setSelectedOrderId] = useState<number | null>(null)

  return (
    <main>
      <h1>ReleaseOrder Demo</h1>
      <CreateOrderForm onOrderCreated={() => setRefreshToken((t) => t + 1)} />
      <OrderList
        refreshToken={refreshToken}
        selectedOrderId={selectedOrderId}
        onSelectOrder={setSelectedOrderId}
      />
      {selectedOrderId !== null && <ReleaseOrderPanel orderId={selectedOrderId} />}
    </main>
  )
}

export default App
