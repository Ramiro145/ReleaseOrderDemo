import { ApiError } from '../api/client'

interface ErrorBannerProps {
  error: ApiError | null
}

export function ErrorBanner({ error }: ErrorBannerProps) {
  if (!error) return null

  const label = error.status === 0 ? 'Error de red' : `Error ${error.status}`

  return (
    <div
      role="alert"
      style={{
        border: '1px solid #c00',
        background: '#fee',
        color: '#900',
        padding: '8px 12px',
        margin: '8px 0',
        borderRadius: 4,
      }}
    >
      <strong>{label}:</strong> {error.message}
    </div>
  )
}
