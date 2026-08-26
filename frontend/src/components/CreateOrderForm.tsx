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

const inputClass =
  'mt-1 w-full rounded-md border border-slate-300 px-3 py-1.5 text-sm text-slate-900 shadow-sm outline-none transition focus:border-indigo-400 focus:ring-2 focus:ring-indigo-100'

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
    <section className="rounded-lg border border-slate-200 bg-white p-5 shadow-sm">
      <h2 className="text-lg font-semibold text-slate-900">Crear orden</h2>
      <ErrorBanner error={error} />
      <form onSubmit={handleSubmit} className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2">
        <label className="block text-sm font-medium text-slate-700">
          Código
          <input className={inputClass} value={form.orderCode} onChange={(e) => update('orderCode', e.target.value)} />
        </label>
        <label className="block text-sm font-medium text-slate-700">
          Producto (ProductId)
          <input
            className={inputClass}
            type="number"
            value={form.productId}
            onChange={(e) => update('productId', e.target.value)}
          />
        </label>
        <label className="block text-sm font-medium text-slate-700">
          Cantidad
          <input
            className={inputClass}
            type="number"
            value={form.quantity}
            onChange={(e) => update('quantity', e.target.value)}
          />
        </label>
        <label className="block text-sm font-medium text-slate-700">
          Monto
          <input
            className={inputClass}
            type="number"
            value={form.amount}
            onChange={(e) => update('amount', e.target.value)}
          />
        </label>
        <label className="block text-sm font-medium text-slate-700 sm:col-span-2">
          Dirección
          <input className={inputClass} value={form.address} onChange={(e) => update('address', e.target.value)} />
        </label>
        <div className="sm:col-span-2">
          <button
            type="submit"
            disabled={submitting}
            className="rounded-md bg-indigo-600 px-4 py-1.5 text-sm font-medium text-white transition hover:bg-indigo-500 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {submitting ? 'Creando...' : 'Crear orden'}
          </button>
        </div>
      </form>
    </section>
  )
}
