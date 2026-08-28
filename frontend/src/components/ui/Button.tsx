import type { ButtonHTMLAttributes } from 'react'

type Variant = 'primary' | 'danger' | 'quiet'

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  loading?: boolean
  loadingLabel?: string
}

// Pensados para vivir sobre el mostrador oscuro. Los botones del ticket
// (Confirmar / Rechazar) se estilan aparte en DecisionGate, sobre papel.
const VARIANT_CLASSES: Record<Variant, string> = {
  primary: 'bg-ledger text-paper hover:bg-ledger-bright',
  danger: 'bg-inkred text-paper hover:bg-inkred-bright',
  quiet:
    'bg-transparent text-paper/75 ring-1 ring-inset ring-counter-line hover:bg-white/5 hover:text-paper',
}

export function Button({
  variant = 'quiet',
  loading = false,
  loadingLabel,
  disabled,
  children,
  className = '',
  ...rest
}: ButtonProps) {
  return (
    <button
      type="button"
      disabled={disabled || loading}
      className={`inline-flex cursor-pointer items-center justify-center gap-2 rounded-md px-4 py-2 text-sm font-medium transition disabled:cursor-not-allowed disabled:opacity-45 ${VARIANT_CLASSES[variant]} ${className}`}
      {...rest}
    >
      {loading ? (loadingLabel ?? children) : children}
    </button>
  )
}
