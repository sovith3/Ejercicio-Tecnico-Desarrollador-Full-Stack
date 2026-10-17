/// <reference types="vite/client" />

/**
 * Variables que el panel consume desde el .env de la raiz. Vite las incrusta en el bundle en tiempo de
 * compilacion, por eso se declaran aqui: asi un nombre mal escrito falla al compilar y no en produccion.
 */
interface ImportMetaEnv {
  /** Ruta base de la API. En contenedor se usa "/api" (mismo origen a traves del proxy de nginx). */
  readonly VITE_API_BASE_URL?: string
  /** Tiempo maximo de espera de cada peticion, en milisegundos. */
  readonly VITE_API_TIMEOUT_MS?: string
  /** Titulo visible del panel. */
  readonly VITE_APP_TITLE?: string
  /** Usuario administrador precargado en el formulario de acceso. */
  readonly VITE_ADMIN_USER?: string
  /** Contrasena precargada en el formulario de acceso (solo demostracion). */
  readonly VITE_ADMIN_PASSWORD?: string
  /** Puerto del servidor de desarrollo de Vite. */
  readonly VITE_DEV_PORT?: string
  /** Destino del proxy de desarrollo hacia la API .NET. */
  readonly VITE_API_PROXY_TARGET?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
