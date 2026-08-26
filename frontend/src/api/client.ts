const API_URL = import.meta.env.VITE_API_URL ?? 'http://localhost:5000'

export class ApiError extends Error {
  status: number
  body: unknown

  constructor(status: number, message: string, body: unknown) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.body = body
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response
  try {
    response = await fetch(`${API_URL}${path}`, {
      ...init,
      headers: {
        'Content-Type': 'application/json',
        ...init?.headers,
      },
    })
  } catch (err) {
    throw new ApiError(0, 'No se pudo conectar con la API. ¿Está corriendo?', err)
  }

  const text = await response.text()
  let body: unknown
  try {
    body = text ? JSON.parse(text) : undefined
  } catch {
    body = text
  }

  if (!response.ok) {
    const message = extractErrorMessage(body) ?? `Error ${response.status}`
    throw new ApiError(response.status, message, body)
  }

  return body as T
}

function extractErrorMessage(body: unknown): string | undefined {
  if (body && typeof body === 'object') {
    // Backend endpoints devuelven { error: "..." } (camelCase de Error).
    // Results.Problem() devuelve ProblemDetails: { detail, title, ... }.
    const record = body as Record<string, unknown>
    for (const key of ['error', 'detail', 'title']) {
      const value = record[key]
      if (typeof value === 'string' && value.trim()) return value
    }
  }
  return undefined
}

export const apiClient = {
  get: <T>(path: string) => request<T>(path, { method: 'GET' }),
  post: <T>(path: string, data?: unknown) =>
    request<T>(path, {
      method: 'POST',
      body: data !== undefined ? JSON.stringify(data) : undefined,
    }),
}
