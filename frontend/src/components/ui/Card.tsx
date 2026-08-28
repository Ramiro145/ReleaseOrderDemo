import type { ReactNode } from 'react'

interface CardProps {
  children: ReactNode
  /** `panel` = superficie oscura del mostrador. `paper` = papel de ticket. */
  variant?: 'panel' | 'paper'
  className?: string
}

const VARIANT_CLASSES = {
  panel: 'rounded-xl bg-counter-raised p-5 ring-1 ring-inset ring-counter-line',
  paper: 'bg-paper text-ink thermal-grain',
} as const

export function Card({ children, variant = 'panel', className = '' }: CardProps) {
  return <section className={`${VARIANT_CLASSES[variant]} ${className}`}>{children}</section>
}
