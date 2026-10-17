import { useState } from 'react'
import { useHealth, useLogin } from './api/hooks'
import { clearSession, loadSession } from './api/session'
import { Badge, Button, ErrorNote, Field, inputClasses } from './components/ui'
import { appConfig } from './config'
import DashboardPanel from './panels/DashboardPanel'
import InvoicesPanel from './panels/InvoicesPanel'
import ReportsPanel from './panels/ReportsPanel'

const tabs = [
  { key: 'dashboard', label: 'Panel' },
  { key: 'invoices', label: 'Facturas' },
  { key: 'reports', label: 'Reportes' }
] as const

type TabKey = (typeof tabs)[number]['key']

/**
 * Shell del panel: cabecera con el estado de la API (sqlite, redis y archivo de origen), sesion JWT para
 * las escrituras y navegacion entre los tres modulos.
 */
export default function App() {
  const [tab, setTab] = useState<TabKey>('dashboard')
  const [session, setSession] = useState(loadSession)
  const [showLogin, setShowLogin] = useState(false)
  const [credentials, setCredentials] = useState({
    username: appConfig.demoUser,
    password: appConfig.demoPassword
  })

  const health = useHealth()
  const login = useLogin()

  const databaseCheck = health.data?.checks.find((check) => check.name === 'sqlite')
  const cacheCheck = health.data?.checks.find((check) => check.name === 'cache')
  const healthTone = health.data?.status === 'Healthy' ? 'good' : health.data ? 'warn' : 'neutral'

  const signIn = () =>
    login.mutate(credentials, {
      onSuccess: (data) => {
        setSession({ token: data.accessToken, user: data.user.displayName || data.user.username })
        setShowLogin(false)
        login.reset()
      }
    })

  const signOut = () => {
    clearSession()
    setSession({ token: null, user: null })
  }

  return (
    <div className="mx-auto flex min-h-full w-full max-w-7xl flex-col gap-4 p-4 lg:p-6">
      <header className="rounded-card border border-line bg-surface px-4 py-3 shadow-panel">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h1 className="text-base font-semibold">{appConfig.title}</h1>
            <p className="mt-0.5 text-xs text-muted">
              API en <span className="font-mono">{appConfig.apiBaseUrl}</span> · panel React + Vite contra
              .NET 8, SQLite y Redis
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <Badge tone={healthTone}>
              {health.data ? `API ${health.data.status}` : 'API sin respuesta'}
            </Badge>
            {databaseCheck && (
              <Badge tone={databaseCheck.status === 'Healthy' ? 'good' : 'bad'}>
                SQLite · {databaseCheck.data?.invoices ?? '?'} facturas
              </Badge>
            )}
            {cacheCheck && (
              <Badge tone={cacheCheck.status === 'Healthy' ? 'good' : 'warn'}>
                Cache · {cacheCheck.data?.provider ?? '?'}
              </Badge>
            )}

            {session.user ? (
              <>
                <span className="text-xs text-muted">{session.user}</span>
                <Button onClick={signOut}>Salir</Button>
              </>
            ) : (
              <Button variant="primary" onClick={() => setShowLogin((current) => !current)}>
                {showLogin ? 'Cancelar' : 'Iniciar sesion'}
              </Button>
            )}
          </div>
        </div>

        {showLogin && (
          <form
            className="mt-3 flex flex-wrap items-end gap-3 border-t border-line pt-3"
            onSubmit={(event) => {
              event.preventDefault()
              signIn()
            }}
          >
            <Field label="Usuario">
              <input
                className={inputClasses}
                value={credentials.username}
                onChange={(event) =>
                  setCredentials((current) => ({ ...current, username: event.target.value }))
                }
              />
            </Field>
            <Field label="Contrasena">
              <input
                className={inputClasses}
                type="password"
                value={credentials.password}
                onChange={(event) =>
                  setCredentials((current) => ({ ...current, password: event.target.value }))
                }
              />
            </Field>
            <Button type="submit" variant="primary" disabled={login.isPending}>
              {login.isPending ? 'Entrando…' : 'Entrar'}
            </Button>
            <p className="text-2xs text-muted">
              El token habilita notas de credito, pagos e importaciones.
            </p>
          </form>
        )}

        {login.error ? (
          <div className="mt-3">
            <ErrorNote error={login.error} />
          </div>
        ) : null}
      </header>

      <nav className="flex flex-wrap gap-2">
        {tabs.map((item) => (
          <Button
            key={item.key}
            variant={tab === item.key ? 'primary' : 'subtle'}
            onClick={() => setTab(item.key)}
          >
            {item.label}
          </Button>
        ))}
      </nav>

      <main className="flex-1">
        {tab === 'dashboard' && <DashboardPanel />}
        {tab === 'invoices' && <InvoicesPanel token={session.token} />}
        {tab === 'reports' && <ReportsPanel token={session.token} />}
      </main>

      <footer className="pb-2 text-2xs text-muted">
        Documentacion interactiva en <span className="font-mono">/swagger</span> de la API. Cache de
        lectura sobre Redis con degradacion a memoria y clave de origen{' '}
        <span className="font-mono">/api</span> inyectada por VITE_API_BASE_URL.
      </footer>
    </div>
  )
}
