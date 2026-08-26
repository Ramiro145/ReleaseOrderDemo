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
      className="my-2 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700"
    >
      <strong className="font-semibold">{label}:</strong> {error.message}
    </div>
  )
}
