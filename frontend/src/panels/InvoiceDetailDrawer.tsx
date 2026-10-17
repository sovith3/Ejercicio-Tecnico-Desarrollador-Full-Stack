import { useState } from 'react'
import { useCreditNoteWriter, useInvoiceDetail, usePaymentWriter } from '../api/hooks'
import { Badge, Button, DefinitionList, ErrorNote, Field, inputClasses } from '../components/ui'
import { formatDate, formatMoney, formatNumber, statusLabel, statusTone } from '../format'

/**
 * Panel lateral de detalle: estados calculados y declarados, detalle de productos, notas de credito,
 * pago registrado y las dos escrituras del enunciado (nota de credito y registro de pago).
 */
export default function InvoiceDetailDrawer({
  number,
  token,
  onClose
}: {
  number: number
  token: string | null
  onClose: () => void
}) {
  const detail = useInvoiceDetail(number)
  const creditNoteWriter = useCreditNoteWriter(token)
  const paymentWriter = usePaymentWriter(token)

  const [amount, setAmount] = useState('')
  const [creditNoteDate, setCreditNoteDate] = useState('')
  const [paymentMethod, setPaymentMethod] = useState('Transferencia electronica')
  const [paymentDate, setPaymentDate] = useState('')

  const invoice = detail.data
  const canWrite = token !== null && invoice !== undefined && invoice.isConsistent

  return (
    <div className="fixed inset-0 z-20 flex justify-end bg-ink/20" role="dialog" aria-modal="true">
      <aside className="h-full w-full max-w-2xl overflow-y-auto border-l border-line bg-surface p-4 shadow-panel">
        <header className="flex items-start justify-between gap-3 border-b border-line pb-3">
          <div>
            <h2 className="text-sm font-semibold">
              {invoice ? `Factura #${invoice.number} · ${invoice.customerName}` : `Factura #${number}`}
            </h2>
            {invoice && (
              <p className="mt-0.5 text-xs text-muted">
                {invoice.customerRun} · {invoice.customerEmail}
              </p>
            )}
          </div>
          <Button variant="ghost" onClick={onClose}>
            Cerrar
          </Button>
        </header>

        {detail.isLoading && <p className="px-1 py-6 text-center text-xs text-muted">Cargando detalle…</p>}
        {detail.error ? <ErrorNote error={detail.error} /> : null}

        {invoice && (
          <div className="flex flex-col gap-4 pt-3">
            <div className="flex flex-wrap items-center gap-2">
              <Badge tone={statusTone(invoice.invoiceStatus)}>
                Factura: {statusLabel(invoice.invoiceStatus)}
              </Badge>
              <Badge tone={statusTone(invoice.paymentStatus)}>
                Cobro: {statusLabel(invoice.paymentStatus)}
              </Badge>
              <Badge tone={invoice.isConsistent ? 'good' : 'bad'}>
                {invoice.isConsistent ? 'Consistente' : 'Inconsistente'}
              </Badge>
              {invoice.hasInconsistentDetailLines && <Badge tone="bad">Detalle con diferencias</Badge>}
            </div>

            {!invoice.isConsistent && (
              <p className="rounded-md border border-rose-300/70 bg-rose-50 px-3 py-2 text-xs text-rose-700">
                La factura queda fuera del sistema activo: no admite notas de credito ni pagos hasta que
                el origen se corrija y se reimporte.
              </p>
            )}

            <DefinitionList
              items={[
                ['Emision', formatDate(invoice.invoiceDate)],
                ['Vencimiento', formatDate(invoice.paymentDueDate)],
                ['Dias para vencer', formatNumber(invoice.daysToDue)],
                ['Dias de atraso', formatNumber(invoice.daysOverdue)],
                ['Total declarado', formatMoney(invoice.totalAmount)],
                ['Suma del detalle', formatMoney(invoice.detailsSubtotalSum)],
                ['Notas de credito', formatMoney(invoice.creditNotesTotal)],
                ['Saldo por cobrar', formatMoney(invoice.outstandingBalance)],
                [
                  'Estado declarado',
                  `${statusLabel(invoice.declaredStatus)}${invoice.isDeclaredStatusDivergent ? ' (divergente)' : ''}`
                ],
                [
                  'Cobro declarado',
                  `${statusLabel(invoice.declaredPaymentStatus)}${invoice.isDeclaredPaymentStatusDivergent ? ' (divergente)' : ''}`
                ]
              ]}
            />

            <section>
              <h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-muted">Productos</h3>
              <table className="w-full text-xs">
                <thead className="text-2xs uppercase tracking-wide text-muted">
                  <tr>
                    <th className="py-1 text-left">Producto</th>
                    <th className="py-1 text-right">Precio</th>
                    <th className="py-1 text-right">Cantidad</th>
                    <th className="py-1 text-right">Subtotal</th>
                  </tr>
                </thead>
                <tbody>
                  {invoice.details.map((line) => (
                    <tr key={line.productName} className="border-t border-line">
                      <td className="py-1.5">{line.productName}</td>
                      <td className="tabular py-1.5 text-right">{formatMoney(line.unitPrice)}</td>
                      <td className="tabular py-1.5 text-right">{formatNumber(line.quantity)}</td>
                      <td
                        className={`tabular py-1.5 text-right ${line.hasInconsistentSubtotal ? 'text-rose-700' : ''}`}
                      >
                        {formatMoney(line.subtotal)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </section>

            <section>
              <h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-muted">
                Notas de credito ({formatNumber(invoice.creditNotes.length)})
              </h3>
              {invoice.creditNotes.length === 0 ? (
                <p className="text-xs text-muted">Sin notas de credito emitidas.</p>
              ) : (
                <ul className="flex flex-col gap-1 text-xs">
                  {invoice.creditNotes.map((note) => (
                    <li
                      key={note.id}
                      className="flex items-center justify-between gap-2 border-b border-line pb-1 last:border-0"
                    >
                      <span>
                        N° {formatNumber(note.number)} · {formatDate(note.creditNoteDate)}
                      </span>
                      <span className="tabular font-medium">-{formatMoney(note.amount)}</span>
                    </li>
                  ))}
                </ul>
              )}
            </section>

            <section>
              <h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-muted">
                Pago registrado
              </h3>
              {invoice.payment ? (
                <p className="text-xs">
                  {invoice.payment.paymentMethod} · {formatDate(invoice.payment.paymentDate)}
                </p>
              ) : (
                <p className="text-xs text-muted">
                  Sin pago registrado. El pago cubre el saldo por cobrar completo.
                </p>
              )}
            </section>

            <section className="grid gap-3 sm:grid-cols-2">
              <form
                className="rounded-card border border-line bg-raised p-3"
                onSubmit={(event) => {
                  event.preventDefault()
                  const value = Number(amount)

                  if (!Number.isFinite(value) || value <= 0) {
                    return
                  }

                  creditNoteWriter.mutate({
                    number,
                    amount: value,
                    creditNoteDate: creditNoteDate || undefined
                  })
                }}
              >
                <h3 className="text-xs font-semibold uppercase tracking-wide text-muted">
                  Emitir nota de credito
                </h3>
                <Field label="Monto" className="mt-2">
                  <input
                    className={inputClasses}
                    type="number"
                    min="0.00001"
                    step="0.00001"
                    placeholder="0,00"
                    value={amount}
                    onChange={(event) => setAmount(event.target.value)}
                  />
                </Field>
                <Field label="Fecha (opcional)" className="mt-2">
                  <input
                    className={inputClasses}
                    type="date"
                    value={creditNoteDate}
                    onChange={(event) => setCreditNoteDate(event.target.value)}
                  />
                </Field>
                <Button
                  type="submit"
                  variant="primary"
                  className="mt-2"
                  disabled={!canWrite || creditNoteWriter.isPending}
                >
                  {creditNoteWriter.isPending ? 'Emitiendo…' : 'Emitir nota'}
                </Button>
              </form>

              <form
                className="rounded-card border border-line bg-raised p-3"
                onSubmit={(event) => {
                  event.preventDefault()

                  if (!paymentMethod.trim()) {
                    return
                  }

                  paymentWriter.mutate({
                    number,
                    paymentMethod: paymentMethod.trim(),
                    paymentDate: paymentDate || undefined
                  })
                }}
              >
                <h3 className="text-xs font-semibold uppercase tracking-wide text-muted">
                  Registrar pago
                </h3>
                <p className="mt-1 text-2xs text-muted">
                  Cubre el saldo pendiente ({formatMoney(invoice.outstandingBalance)}).
                </p>
                <Field label="Medio de pago" className="mt-2">
                  <input
                    className={inputClasses}
                    maxLength={60}
                    value={paymentMethod}
                    onChange={(event) => setPaymentMethod(event.target.value)}
                  />
                </Field>
                <Field label="Fecha (opcional)" className="mt-2">
                  <input
                    className={inputClasses}
                    type="date"
                    value={paymentDate}
                    onChange={(event) => setPaymentDate(event.target.value)}
                  />
                </Field>
                <Button
                  type="submit"
                  variant="primary"
                  className="mt-2"
                  disabled={!canWrite || paymentWriter.isPending}
                >
                  {paymentWriter.isPending ? 'Registrando…' : 'Registrar pago'}
                </Button>
              </form>
            </section>

            {!canWrite && invoice.isConsistent && (
              <p className="text-2xs text-muted">
                Inicia sesion en la barra superior para habilitar las escrituras.
              </p>
            )}
            {creditNoteWriter.error ? <ErrorNote error={creditNoteWriter.error} /> : null}
            {paymentWriter.error ? <ErrorNote error={paymentWriter.error} /> : null}
            {creditNoteWriter.isSuccess && (
              <p className="text-2xs text-accent">Nota de credito emitida y estados recalculados.</p>
            )}
            {paymentWriter.isSuccess && (
              <p className="text-2xs text-accent">Pago registrado y estado de cobro actualizado.</p>
            )}
            <Button variant="subtle" onClick={onClose}>
              Volver al listado
            </Button>
          </div>
        )}
      </aside>
    </div>
  )
}
