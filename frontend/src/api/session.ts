/**
 * Sesion del panel: el token JWT se conserva en el almacenamiento local del navegador y viaja en la
 * cabecera Authorization de las operaciones de escritura (nota de credito, pago, importacion).
 */
const TOKEN_KEY = 'invoicehub.token'
const USER_KEY = 'invoicehub.user'

export interface Session {
  token: string | null
  user: string | null
}

export const loadSession = (): Session => {
  try {
    return {
      token: window.localStorage.getItem(TOKEN_KEY),
      user: window.localStorage.getItem(USER_KEY)
    }
  } catch {
    return { token: null, user: null }
  }
}

export const saveSession = (token: string, user: string) => {
  try {
    window.localStorage.setItem(TOKEN_KEY, token)
    window.localStorage.setItem(USER_KEY, user)
  } catch {
    // Un navegador con almacenamiento restringido solo pierde la persistencia, no la sesion en curso.
  }
}

export const clearSession = () => {
  try {
    window.localStorage.removeItem(TOKEN_KEY)
    window.localStorage.removeItem(USER_KEY)
  } catch {
    // Ver saveSession.
  }
}
