import { ApiError } from '../api/client'

interface ErrorBannerProps {
  error: ApiError | null
  /** true si va sobre el papel del ticket en vez del mostrador oscuro */
  onPaper?: boolean
}

export function ErrorBanner({ error, onPaper = false }: ErrorBannerProps) {
  if (!error) return null

  const label = error.status === 0 ? 'Sin conexión' : `Error ${error.status}`
  const skin = onPaper
    ? 'bg-inkred/8 text-inkred ring-inkred/30'
    : 'bg-inkred/12 text-inkred-bright ring-inkred/40'

  return (
    <div role="alert" className={`my-2 rounded-md px-3 py-2 text-sm ring-1 ring-inset ${skin}`}>
      <strong className="font-semibold">{label}:</strong> {error.message}
    </div>
  )
}
