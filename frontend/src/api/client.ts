import { appConfig, apiUrl } from '../config'

/** Error normalizado de la API: conserva el ProblemDetails que publica el backend. */
export class ApiError extends Error {
  readonly status: number
  readonly traceId?: string
  readonly businessCode?: string

  constructor(message: string, status: number, traceId?: string, businessCode?: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.traceId = traceId
    this.businessCode = businessCode
  }
}

interface ProblemDetailsPayload {
  title?: string
  detail?: string
  traceId?: string
  businessCode?: string
}

interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE'
  body?: unknown
  token?: string | null
}

const readProblem = async (response: Response): Promise<ProblemDetailsPayload> => {
  try {
    return (await response.json()) as ProblemDetailsPayload
  } catch {
    return {}
  }
}

/**
 * Cliente HTTP unico del panel. La URL base sale de VITE_API_BASE_URL: en el contenedor es "/api" (mismo
 * origen servido por nginx, sin CORS) y en desarrollo el proxy de Vite apunta a la API .NET.
 */
export const request = async <T>(path: string, options: RequestOptions = {}): Promise<T> => {
  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(), appConfig.timeoutMs)

  try {
    const response = await fetch(apiUrl(path), {
      method: options.method ?? 'GET',
      headers: {
        Accept: 'application/json',
        ...(options.body === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...(options.token ? { Authorization: `Bearer ${options.token}` } : {})
      },
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: controller.signal
    })

    if (!response.ok) {
      const problem = await readProblem(response)
      throw new ApiError(
        problem.detail ?? problem.title ?? `La API respondio ${response.status}.`,
        response.status,
        problem.traceId,
        problem.businessCode
      )
    }

    if (response.status === 204) {
      return undefined as T
    }

    const payload = await response.text()
    return (payload ? JSON.parse(payload) : undefined) as T
  } catch (error) {
    if (error instanceof ApiError) {
      throw error
    }

    if (error instanceof Error && error.name === 'AbortError') {
      throw new ApiError(`La peticion a ${path} excedio ${appConfig.timeoutMs} ms.`, 0)
    }

    throw new ApiError(error instanceof Error ? error.message : 'Error de red desconocido.', 0)
  } finally {
    clearTimeout(timer)
  }
}

/** Construye una cadena de consulta ignorando los valores vacios. */
export const query = (params: Record<string, string | number | boolean | undefined | null>) => {
  const search = new URLSearchParams()

  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') {
      continue
    }

    search.set(key, String(value))
  }

  const serialized = search.toString()
  return serialized ? `?${serialized}` : ''
}
