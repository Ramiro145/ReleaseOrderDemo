import { Button } from './ui/Button'

interface HeaderProps {
  connected: boolean
  onRefresh: () => void
  refreshing: boolean
}

export function Header({ connected, onRefresh, refreshing }: HeaderProps) {
  return (
    <header className="border-b border-counter-line">
      <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-4 px-4 py-4 sm:px-6">
        <div>
          <h1 className="font-display text-2xl font-bold tracking-tight text-paper">La Placa</h1>
          <p className="mt-0.5 font-mono text-[0.6875rem] uppercase tracking-[0.2em] text-thermal">
            mostrador de pedidos · demo temporal
          </p>
        </div>

        <div className="flex items-center gap-3">
          <span className="inline-flex items-center gap-1.5 font-mono text-xs text-thermal">
            <span
              aria-hidden
              className={`h-1.5 w-1.5 rounded-full ${
                connected ? 'bg-ledger-bright' : 'bg-inkred-bright'
              }`}
            />
            {connected ? 'conectado' : 'sin conexión'}
          </span>
          <Button onClick={onRefresh} loading={refreshing} loadingLabel="actualizando…">
            actualizar
          </Button>
        </div>
      </div>
    </header>
  )
}
