import { useState } from 'react'
import type { FormEvent } from 'react'
import { createOrder } from '../api/orders'
import { ApiError } from '../api/client'
import { ErrorBanner } from './ErrorBanner'

interface CreateOrderFormProps {
  onOrderCreated: () => void
}

const emptyForm = {
  orderCode: '',
  productId: '',
  quantity: '',
  amount: '',
  address: '',
}

export function CreateOrderForm({ onOrderCreated }: CreateOrderFormProps) {
  const [form, setForm] = useState(emptyForm)
  const [error, setError] = useState<ApiError | null>(null)
  const [submitting, setSubmitting] = useState(false)

  function update<K extends keyof typeof emptyForm>(key: K, value: string) {
    setForm((prev) => ({ ...prev, [key]: value }))
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)

    if (!form.orderCode.trim() || !form.productId || !form.quantity || !form.amount || !form.address.trim()) {
      setError(new ApiError(0, 'Todos los campos son obligatorios.', null))
      return
    }

    setSubmitting(true)
    try {
      await createOrder({
        orderCode: form.orderCode.trim(),
        productId: Number(form.productId),
        quantity: Number(form.quantity),
        amount: Number(form.amount),
        address: form.address.trim(),
      })
      setForm(emptyForm)
      onOrderCreated()
    } catch (err) {
      setError(err instanceof ApiError ? err : new ApiError(0, 'Error inesperado', err))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <section>
      <h2>Crear orden</h2>
      <ErrorBanner error={error} />
      <form onSubmit={handleSubmit}>
        <div>
          <label>
            Código
            <input value={form.orderCode} onChange={(e) => update('orderCode', e.target.value)} />
          </label>
        </div>
        <div>
          <label>
            Producto (ProductId)
            <input
              type="number"
              value={form.productId}
              onChange={(e) => update('productId', e.target.value)}
            />
          </label>
        </div>
        <div>
          <label>
            Cantidad
            <input type="number" value={form.quantity} onChange={(e) => update('quantity', e.target.value)} />
          </label>
        </div>
        <div>
          <label>
            Monto
            <input type="number" value={form.amount} onChange={(e) => update('amount', e.target.value)} />
          </label>
        </div>
        <div>
          <label>
            Dirección
            <input value={form.address} onChange={(e) => update('address', e.target.value)} />
          </label>
        </div>
        <button type="submit" disabled={submitting}>
          {submitting ? 'Creando...' : 'Crear orden'}
        </button>
      </form>
    </section>
  )
}
