import clsx from 'clsx'
import type { ButtonHTMLAttributes, ReactNode } from 'react'
import { ApiError } from '../api/client'
import type { Tone } from '../format'

const toneClasses: Record<Tone, string> = {
  neutral: 'border-line bg-raised text-muted',
  good: 'border-accent/40 bg-accent-soft text-accent',
  warn: 'border-amber-300/70 bg-amber-50 text-amber-700',
  bad: 'border-rose-300/70 bg-rose-50 text-rose-700'
}

export function Badge({ tone = 'neutral', children }: { tone?: Tone; children: ReactNode }) {
  return (
    <span
      className={clsx(
        'inline-flex items-center rounded-full border px-2 py-0.5 text-2xs font-medium',
        toneClasses[tone]
      )}
    >
      {children}
    </span>
  )
}

export function Card({
  title,
  subtitle,
  actions,
  children,
  className
}: {
  title?: string
  subtitle?: string
  actions?: ReactNode
  children: ReactNode
  className?: string
}) {
  return (
    <section
      className={clsx('rounded-card border border-line bg-surface shadow-panel', className)}
    >
      {(title || actions) && (
        <header className="flex flex-wrap items-start justify-between gap-3 border-b border-line px-4 py-3">
          <div>
            {title && <h2 className="text-sm font-semibold">{title}</h2>}
            {subtitle && <p className="mt-0.5 text-xs text-muted">{subtitle}</p>}
          </div>
          {actions}
        </header>
      )}
      <div className="px-4 py-3">{children}</div>
    </section>
  )
}

export function Stat({
  label,
  value,
  hint,
  tone = 'neutral'
}: {
  label: string
  value: string
  hint?: string
  tone?: Tone
}) {
  return (
    <div
      className={clsx(
        'rounded-card border bg-surface px-4 py-3 shadow-panel',
        tone === 'bad' && 'border-rose-300/70',
        tone === 'good' && 'border-accent/40',
        tone === 'warn' && 'border-amber-300/70',
        tone === 'neutral' && 'border-line'
      )}
    >
      <p className="text-2xs font-medium uppercase tracking-wide text-muted">{label}</p>
      <p className="tabular mt-1 text-xl font-semibold">{value}</p>
      {hint && <p className="mt-0.5 text-xs text-muted">{hint}</p>}
    </div>
  )
}

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: 'primary' | 'subtle' | 'ghost'
}

export function Button({ variant = 'subtle', className, ...props }: ButtonProps) {
  return (
    <button
      {...props}
      className={clsx(
        'inline-flex items-center justify-center gap-1.5 rounded-md border px-3 py-1.5 text-xs font-medium transition',
        'disabled:cursor-not-allowed disabled:opacity-50',
        variant === 'primary' && 'border-accent bg-accent text-accent-ink hover:opacity-90',
        variant === 'subtle' && 'border-line bg-surface text-ink hover:bg-raised',
        variant === 'ghost' && 'border-transparent bg-transparent text-muted hover:text-ink',
        className
      )}
    />
  )
}

export const inputClasses =
  'rounded-md border border-line bg-surface px-2.5 py-1.5 text-sm font-normal normal-case tracking-normal text-ink outline-none focus:border-accent'

export function Field({
  label,
  children,
  className
}: {
  label: string
  children: ReactNode
  className?: string
}) {
  return (
    <label
      className={clsx(
        'flex flex-col gap-1 text-2xs font-medium uppercase tracking-wide text-muted',
        className
      )}
    >
      {label}
      {children}
    </label>
  )
}

export function ErrorNote({ error }: { error: unknown }) {
  const message = error instanceof Error ? error.message : 'Error inesperado.'
  const status = error instanceof ApiError ? error.status : undefined

  return (
    <p className="rounded-md border border-rose-300/70 bg-rose-50 px-3 py-2 text-xs text-rose-700">
      {status ? `[${status}] ` : ''}
      {message}
    </p>
  )
}

export function Loading({ label = 'Cargando…' }: { label?: string }) {
  return <p className="px-1 py-6 text-center text-xs text-muted">{label}</p>
}

export function EmptyState({ label }: { label: string }) {
  return <p className="px-1 py-6 text-center text-xs text-muted">{label}</p>
}

export function DefinitionList({ items }: { items: Array<[string, ReactNode]> }) {
  return (
    <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-xs">
      {items.map(([term, value]) => (
        <div key={term} className="flex flex-col">
          <dt className="text-2xs uppercase tracking-wide text-muted">{term}</dt>
          <dd className="tabular font-medium">{value}</dd>
        </div>
      ))}
    </dl>
  )
}
