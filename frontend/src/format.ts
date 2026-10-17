import type { InvoiceStatus, PaymentStatus } from './api/types'

const moneyFormatter = new Intl.NumberFormat('es-CL', {
  style: 'currency',
  currency: 'CLP',
  minimumFractionDigits: 0,
  maximumFractionDigits: 0
})

const numberFormatter = new Intl.NumberFormat('es-CL')

const percentFormatter = new Intl.NumberFormat('es-CL', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2
})

/**
 * Montos en pesos chilenos. La API, los calculos del dominio y las rutinas SQL conservan hasta cinco
 * decimales; el panel presenta el monto redondeado a la unidad de peso (el primer decimal decide el
 * redondeo: 0,4 -> 0 y 0,5 -> 1) con punto de miles: 1.234.568.
 */
export const formatMoney = (value: number | null | undefined) => moneyFormatter.format(value ?? 0)

export const formatNumber = (value: number | null | undefined) => numberFormatter.format(value ?? 0)

export const formatPercent = (value: number | null | undefined) =>
  `${percentFormatter.format(value ?? 0)} %`

/** Fechas ISO (yyyy-MM-dd) al formato local dd-MM-yyyy, sin depender de la zona del navegador. */
export const formatDate = (value: string | null | undefined) => {
  if (!value) {
    return '—'
  }

  const [year, month, day] = value.slice(0, 10).split('-')
  return year && month && day ? `${day}-${month}-${year}` : value
}

export const invoiceStatusLabels: Record<InvoiceStatus, string> = {
  Issued: 'Emitida',
  Partial: 'Parcial',
  Cancelled: 'Anulada'
}

export const paymentStatusLabels: Record<PaymentStatus, string> = {
  Pending: 'Pendiente',
  Overdue: 'Vencida',
  Paid: 'Pagada'
}

/** Etiqueta legible de los estados del dominio o de las etiquetas que ya traduce la API. */
export const statusLabel = (value: string | null | undefined) => {
  if (!value) {
    return '—'
  }

  return (
    invoiceStatusLabels[value as InvoiceStatus] ??
    paymentStatusLabels[value as PaymentStatus] ??
    value
  )
}

export type Tone = 'neutral' | 'good' | 'warn' | 'bad'

export const statusTone = (value: string | null | undefined): Tone => {
  switch (value) {
    case 'Paid':
    case 'Issued':
      return 'good'
    case 'Partial':
    case 'Pending':
      return 'warn'
    case 'Overdue':
    case 'Cancelled':
      return 'bad'
    default:
      return 'neutral'
  }
}
