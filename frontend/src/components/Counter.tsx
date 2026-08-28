import { useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { createOrder } from '../api/orders'
import { ApiError } from '../api/client'
import { CATALOG } from '../catalog'
import { formatAmount } from '../lib/format'
import { Card } from './ui/Card'
import { Button } from './ui/Button'
import { ErrorBanner } from './ErrorBanner'

interface CounterProps {
  onOrderPlaced: (orderId: number) => void
}

const inputClass =
  'mt-1 w-full rounded-md bg-counter/60 px-3 py-2 text-sm text-paper ring-1 ring-inset ring-counter-line outline-none transition placeholder:text-thermal focus:ring-ledger'

function newOrderCode(): string {
  return `ORD-${Date.now().toString().slice(-6)}`
}

export function Counter({ onOrderPlaced }: CounterProps) {
  const [orderCode, setOrderCode] = useState(newOrderCode)
  const [productId, setProductId] = useState(CATALOG[0].productId)
  const [quantity, setQuantity] = useState('1')
  const [amount, setAmount] = useState('')
  const [address, setAddress] = useState('')
  const [error, setError] = useState<ApiError | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const suggested = useMemo(() => {
    const unit = CATALOG.find((p) => p.productId === productId)?.unitPrice ?? 0
    const qty = Number(quantity)
    return Number.isFinite(qty) && qty > 0 ? unit * qty : 0
  }, [productId, quantity])

  const charge = amount.trim() === '' ? suggested : Number(amount)

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)

    const qty = Number(quantity)
    if (!Number.isInteger(qty) || qty <= 0) {
      setError(new ApiError(0, 'La cantidad tiene que ser un entero mayor que cero.', null))
      return
    }
    if (!address.trim()) {
      setError(new ApiError(0, 'Falta la dirección de envío.', null))
      return
    }
    if (!Number.isFinite(charge)) {
      setError(new ApiError(0, 'El importe a cobrar no es un número válido.', null))
      return
    }

    setSubmitting(true)
    try {
      const result = await createOrder({
        orderCode: orderCode.trim(),
        productId,
        quantity: qty,
        amount: charge,
        address: address.trim(),
      })
      setOrderCode(newOrderCode())
      setQuantity('1')
      setAmount('')
      setAddress('')
      onOrderPlaced(result.orderId)
    } catch (err) {
      setError(err instanceof ApiError ? err : new ApiError(0, 'Error inesperado', err))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Card>
      <h2 className="font-mono text-[0.6875rem] font-semibold uppercase tracking-[0.2em] text-thermal">
        Mostrador
      </h2>

      <form onSubmit={handleSubmit} className="mt-3 flex flex-col gap-4">
        <fieldset className="flex flex-col gap-1.5">
          <legend className="sr-only">Producto</legend>
          {CATALOG.map((product) => {
            const selected = product.productId === productId
            return (
              <label
                key={product.productId}
                className={`flex cursor-pointer items-center gap-3 rounded-md px-3 py-2 text-sm ring-1 ring-inset transition has-[:focus-visible]:outline has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-brass ${
                  selected
                    ? 'bg-ledger/12 ring-ledger-bright'
                    : 'ring-counter-line hover:ring-paper/25'
                }`}
              >
                <input
                  type="radio"
                  name="product"
                  className="sr-only"
                  checked={selected}
                  onChange={() => setProductId(product.productId)}
                />
                <span className="font-mono text-[0.6875rem] text-thermal">[{product.tag}]</span>
                <span className="flex-1 font-medium text-paper">{product.name}</span>
                <span className="font-mono text-xs text-thermal">
                  {formatAmount(product.unitPrice)}
                </span>
              </label>
            )
          })}
        </fieldset>

        <div className="flex gap-3">
          <label className="w-24 text-sm font-medium text-paper/80">
            Cantidad
            <input
              className={inputClass}
              type="number"
              min="1"
              value={quantity}
              onChange={(e) => setQuantity(e.target.value)}
            />
          </label>
          <label className="flex-1 text-sm font-medium text-paper/80">
            Importe a cobrar
            <input
              className={inputClass}
              type="number"
              inputMode="numeric"
              placeholder={String(suggested)}
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
            />
          </label>
        </div>

        <label className="text-sm font-medium text-paper/80">
          Enviar a
          <input
            className={inputClass}
            value={address}
            onChange={(e) => setAddress(e.target.value)}
            placeholder="calle, número, ciudad"
          />
        </label>

        <details className="rounded-md bg-counter/50 px-3 py-2 text-xs text-thermal ring-1 ring-inset ring-counter-line">
          <summary className="cursor-pointer select-none font-mono uppercase tracking-[0.14em]">
            Atajos para provocar fallos
          </summary>
          <ul className="mt-2 space-y-1 font-mono leading-relaxed">
            <li>importe 999999 → el cobro da timeout y reintenta 3 veces</li>
            <li>importe 0 → el cobro se rechaza sin reintento</li>
            <li>dirección con FAIL → el envío falla y revierte todo</li>
            <li>cantidad mayor al stock → no hay stock y revierte</li>
          </ul>
        </details>

        <ErrorBanner error={error} />

        <div className="flex items-baseline justify-between border-t border-counter-line pt-3 font-mono text-sm">
          <span className="text-thermal">total a cobrar</span>
          <span className="text-paper">{formatAmount(Number.isFinite(charge) ? charge : 0)}</span>
        </div>

        <Button type="submit" variant="primary" loading={submitting} loadingLabel="registrando…">
          Realizar pedido
        </Button>
        <p className="text-center font-mono text-[0.625rem] text-thermal">
          se registrará como {orderCode}
        </p>
      </form>
    </Card>
  )
}
