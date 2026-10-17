import { useState } from 'react'
import {
  useImportRunner,
  useImportSource,
  useInconsistentInvoices,
  useOverdueWithoutActivity,
  usePaymentStatusSummary
} from '../api/hooks'
import { Button, Card, EmptyState, ErrorNote, Field, Loading, Stat, inputClasses } from '../components/ui'
import { formatDate, formatMoney, formatNumber, formatPercent } from '../format'

/**
 * Reportes del enunciado (vencidas sin actividad, estado de cobro e inconsistencias) mas el estado del
 * archivo JSON de origen y la reimportacion idempotente.
 */
export default function ReportsPanel({ token }: { token: string | null }) {
  const [minDaysOverdue, setMinDaysOverdue] = useState(30)
  const [daysDraft, setDaysDraft] = useState('30')

  const paymentSummary = usePaymentStatusSummary()
  const overdue = useOverdueWithoutActivity(minDaysOverdue)
  const inconsistent = useInconsistentInvoices()
  const source = useImportSource()
  const importer = useImportRunner(token)

  return (
    <div className="flex flex-col gap-4">
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Stat
          label="Reporte 1 · vencidas sin actividad"
          value={formatMoney(overdue.data?.totalAmount)}
          hint={`${formatNumber(overdue.data?.totalInvoices)} facturas con mas de ${minDaysOverdue} dias`}
          tone="bad"
        />
        <Stat
          label="Reporte 2 · monto vigente"
          value={formatMoney(paymentSummary.data?.totalAmount)}
          hint={`${formatNumber(paymentSummary.data?.totalInvoices)} facturas activas`}
        />
        <Stat
          label="Reporte 3 · diferencia total"
          value={formatMoney(
            inconsistent.data
              ? inconsistent.data.totalDeclared - inconsistent.data.totalDetailsSubtotal
              : 0
          )}
          hint={`${formatNumber(inconsistent.data?.totalInvoices)} facturas con diferencias`}
          tone="bad"
        />
        <Stat
          label="Origen JSON"
          value={source.data ? (source.data.exists ? 'Disponible' : 'Sin archivo') : '—'}
          hint={source.data ? `${formatNumber(source.data.sizeInBytes)} bytes` : 'Consultando…'}
          tone={source.data?.exists ? 'good' : 'bad'}
        />
      </div>

      <Card
        title="Reporte 1 · Vencidas sin pago ni nota de credito"
        subtitle="Cartera en mora sobre el sistema activo"
        actions={
          <form
            className="flex items-end gap-2"
            onSubmit={(event) => {
              event.preventDefault()
              setMinDaysOverdue(Number(daysDraft) || 30)
            }}
          >
            <Field label="Dias minimos">
              <input
                className={`${inputClasses} w-20`}
                type="number"
                min={1}
                max={3650}
                value={daysDraft}
                onChange={(event) => setDaysDraft(event.target.value)}
              />
            </Field>
            <Button type="submit">Aplicar</Button>
          </form>
        }
      >
        {overdue.isLoading && <Loading />}
        {overdue.error ? <ErrorNote error={overdue.error} /> : null}
        {overdue.data && overdue.data.items.length === 0 && <EmptyState label="Sin facturas en mora." />}
        {overdue.data && overdue.data.items.length > 0 && (
          <div className="max-h-80 overflow-auto">
            <table className="w-full text-xs">
              <thead className="sticky top-0 bg-surface text-2xs uppercase tracking-wide text-muted">
                <tr>
                  <th className="py-1 text-left">Factura</th>
                  <th className="py-1 text-left">Cliente</th>
                  <th className="py-1 text-right">Vencimiento</th>
                  <th className="py-1 text-right">Atraso</th>
                  <th className="py-1 text-right">Monto</th>
                </tr>
              </thead>
              <tbody>
                {overdue.data.items.map((item) => (
                  <tr key={item.number} className="border-t border-line">
                    <td className="tabular py-1.5 font-medium">#{item.number}</td>
                    <td className="py-1.5">{item.customerName}</td>
                    <td className="tabular py-1.5 text-right">{formatDate(item.paymentDueDate)}</td>
                    <td className="tabular py-1.5 text-right text-rose-700">
                      {formatNumber(item.daysOverdue)} d
                    </td>
                    <td className="tabular py-1.5 text-right">{formatMoney(item.totalAmount)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="Reporte 2 · Estado de cobro" subtitle="Totales y porcentajes por estado de pago">
          {paymentSummary.isLoading && <Loading />}
          {paymentSummary.error ? <ErrorNote error={paymentSummary.error} /> : null}
          {paymentSummary.data && (
            <table className="w-full text-xs">
              <thead className="text-2xs uppercase tracking-wide text-muted">
                <tr>
                  <th className="py-1 text-left">Estado</th>
                  <th className="py-1 text-right">Facturas</th>
                  <th className="py-1 text-right">Monto</th>
                  <th className="py-1 text-right">% monto</th>
                  <th className="py-1 text-right">% facturas</th>
                </tr>
              </thead>
              <tbody>
                {paymentSummary.data.items.map((item) => (
                  <tr key={String(item.status ?? item.value)} className="border-t border-line">
                    <td className="py-1.5 font-medium">{item.statusLabel ?? item.label ?? '—'}</td>
                    <td className="tabular py-1.5 text-right">{formatNumber(item.invoiceCount)}</td>
                    <td className="tabular py-1.5 text-right">{formatMoney(item.totalAmount)}</td>
                    <td className="tabular py-1.5 text-right">
                      {formatPercent(item.amountPercentage)}
                    </td>
                    <td className="tabular py-1.5 text-right text-muted">
                      {formatPercent(item.countPercentage)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Card>

        <Card title="Reporte 3 · Inconsistencias" subtitle="Total declarado contra la suma del detalle">
          {inconsistent.isLoading && <Loading />}
          {inconsistent.error ? <ErrorNote error={inconsistent.error} /> : null}
          {inconsistent.data && inconsistent.data.items.length === 0 && (
            <EmptyState label="Todas las facturas cuadran." />
          )}
          {inconsistent.data && inconsistent.data.items.length > 0 && (
            <ul className="flex flex-col gap-2 text-xs">
              {inconsistent.data.items.map((item) => (
                <li key={item.number} className="border-b border-line pb-2 last:border-0">
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-medium">
                      #{item.number} · {item.customerName}
                    </span>
                    <span className="tabular text-rose-700">{formatMoney(item.difference)}</span>
                  </div>
                  <p className="mt-0.5 text-2xs text-muted">{item.reason}</p>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>

      <Card
        title="Origen de datos"
        subtitle="Importacion idempotente del archivo JSON montado en el contenedor"
        actions={
          <Button variant="primary" disabled={importer.isPending} onClick={() => importer.mutate()}>
            {importer.isPending ? 'Importando…' : 'Reimportar origen'}
          </Button>
        }
      >
        {source.isLoading && <Loading />}
        {source.error ? <ErrorNote error={source.error} /> : null}
        {source.data && (
          <div className="flex flex-col gap-2 text-xs">
            <p className="break-all font-mono text-2xs text-muted">{source.data.filePath}</p>
            <p>
              {formatNumber(source.data.persistedInvoices)} facturas almacenadas · ultima importacion:{' '}
              {source.data.lastRun
                ? `${source.data.lastRun.status} (${formatNumber(source.data.lastRun.importedRecords)} importadas, ${formatNumber(source.data.lastRun.inconsistentRecords)} inconsistentes, ${formatNumber(source.data.lastRun.skippedRecords)} omitidas) en ${formatNumber(source.data.lastRun.durationMilliseconds)} ms`
                : 'sin ejecuciones'}
            </p>
            {source.data.lastRun && source.data.lastRun.issues.length > 0 && (
              <details className="text-2xs text-muted">
                <summary className="cursor-pointer">
                  {formatNumber(source.data.lastRun.issues.length)} observaciones de la ultima importacion
                </summary>
                <ul className="mt-1 list-disc pl-4">
                  {source.data.lastRun.issues.slice(0, 12).map((issue) => (
                    <li key={issue}>{issue}</li>
                  ))}
                </ul>
              </details>
            )}
            {importer.error ? <ErrorNote error={importer.error} /> : null}
            {importer.isSuccess && (
              <p className="text-2xs text-accent">
                Reimportacion completada: los registros ya cargados se omiten y solo se incorporan los
                nuevos.
              </p>
            )}
          </div>
        )}
      </Card>
    </div>
  )
}

