import type { ButtonHTMLAttributes } from 'react'

type Variant = 'primary' | 'neutral' | 'ghost'

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  loading?: boolean
  loadingLabel?: string
}

const VARIANT_CLASSES: Record<Variant, string> = {
  primary: 'bg-indigo-600 text-white hover:bg-indigo-500',
  neutral: 'bg-slate-900 text-white hover:bg-slate-700',
  ghost: 'bg-white text-slate-700 ring-1 ring-inset ring-slate-300 hover:bg-slate-50',
}

export function Button({
  variant = 'neutral',
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
      className={`rounded-md px-4 py-1.5 text-sm font-medium transition disabled:cursor-not-allowed disabled:opacity-50 ${VARIANT_CLASSES[variant]} ${className}`}
      {...rest}
    >
      {loading ? loadingLabel ?? children : children}
    </button>
  )
}
