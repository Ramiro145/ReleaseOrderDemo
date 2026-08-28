const currency = new Intl.NumberFormat('es-AR', {
  style: 'currency',
  currency: 'ARS',
  maximumFractionDigits: 0,
})

export function formatAmount(value: number): string {
  return currency.format(value)
}

export function formatClock(date: Date): string {
  return date.toLocaleTimeString('es-AR', { hour12: false })
}
