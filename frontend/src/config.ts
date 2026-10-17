/**
 * Configuracion del panel resuelta desde las variables VITE_* del .env raiz.
 * Ninguna credencial de infraestructura (Redis, base de datos, JWT) llega hasta aqui: Vite solo publica
 * las variables con prefijo VITE_ y el resto queda del lado del contenedor de la API.
 */
const withoutTrailingSlash = (value: string) => value.replace(/\/+$/, '')

const readNumber = (value: string | undefined, fallback: number) => {
  const parsed = Number(value)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

export const appConfig = {
  title: import.meta.env.VITE_APP_TITLE?.trim() || 'Facturia · Control de facturacion',
  apiBaseUrl: withoutTrailingSlash(import.meta.env.VITE_API_BASE_URL?.trim() || '/api'),
  timeoutMs: readNumber(import.meta.env.VITE_API_TIMEOUT_MS, 15_000),
  demoUser: import.meta.env.VITE_ADMIN_USER?.trim() || 'admin',
  demoPassword: import.meta.env.VITE_ADMIN_PASSWORD?.trim() || 'InvoiceHub.2026'
} as const

/** Compone la URL absoluta o relativa de un recurso de la API. */
export const apiUrl = (path: string) =>
  `${appConfig.apiBaseUrl}${path.startsWith('/') ? path : `/${path}`}`
