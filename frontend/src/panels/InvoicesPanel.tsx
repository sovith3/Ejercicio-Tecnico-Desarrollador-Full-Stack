import { useState } from 'react'
import { useInvoices, type InvoiceSearchFilters } from '../api/hooks'
import { Badge, Button, Card, ErrorNote, Field, Loading, inputClasses } from '../components/ui'
import { formatDate, formatMoney, formatNumber, statusLabel, statusTone } from '../format'
import InvoiceDetailDrawer from './InvoiceDetailDrawer'

const emptyFilters: InvoiceSearchFilters = {
  text: '',
  invoiceStatus: '',
  paymentStatus: '',
  onlyActiveSystem: true,
  page: 1,
  pageSize: 10,
  sortBy: 'dueDate',
  sortDirection: 'desc'
}

/** Busqueda y detalle de facturas: filtros, paginacion y panel lateral de acciones. */
export default function InvoicesPanel({ token }: { token: string | null }) {
  const [filters, setFilters] = useState<InvoiceSearchFilters>(emptyFilters)
  const [draft, setDraft] = useState('')
  const [selected, setSelected] = useState<number | null>(null)

  const invoices = useInvoices(filters)

  const patch = (changes: Partial<InvoiceSearchFilters>) =>
    setFilters((current) => ({ ...current, ...changes, page: changes.page ?? 1 }))

  return (
    <div className="flex flex-col gap-4">
      <Card
        title="Busqueda de facturas"
        subtitle="Sistema activo: excluye las inconsistencias y respeta los estados calculados"
      >
        <form
          className="flex flex-wrap items-end gap-3"
          onSubmit={(event) => {
            event.preventDefault()
            patch({ text: draft })
          }}
        >
          <Field label="Texto (numero, cliente, correo)" className="min-w-56 grow">
            <input
              className={inputClasses}
              placeholder="Ej. 42, Perry, forbes.com"
              value={draft}
              onChange={(event) => setDraft(event.target.value)}
            />
          </Field>

          <Field label="Estado factura">
            <select
              className={inputClasses}
              value={filters.invoiceStatus}
              onChange={(event) =>
                patch({ invoiceStatus: event.target.value as InvoiceSearchFilters['invoiceStatus'] })
              }
            >
              <option value="">Todos</option>
              <option value="Issued">Emitida</option>
              <option value="Partial">Parcial</option>
              <option value="Cancelled">Anulada</option>
            </select>
          </Field>

          <Field label="Estado de cobro">
            <select
              className={inputClasses}
              value={filters.paymentStatus}
              onChange={(event) =>
                patch({ paymentStatus: event.target.value as InvoiceSearchFilters['paymentStatus'] })
              }
            >
              <option value="">Todos</option>
              <option value="Pending">Pendiente</option>
              <option value="Overdue">Vencida</option>
              <option value="Paid">Pagada</option>
            </select>
          </Field>

          <Field label="Orden">
            <select
              className={inputClasses}
              value={`${filters.sortBy}:${filters.sortDirection}`}
              onChange={(event) => {
                const [sortBy, sortDirection] = event.target.value.split(':')
                patch({ sortBy, sortDirection: sortDirection === 'asc' ? 'asc' : 'desc' })
              }}
            >
              <option value="dueDate:desc">Vencimiento (reciente)</option>
              <option value="dueDate:asc">Vencimiento (antiguo)</option>
              <option value="number:desc">Numero (mayor)</option>
              <option value="number:asc">Numero (menor)</option>
            </select>
          </Field>

          <Field label="Tamano">
            <select
              className={inputClasses}
              value={filters.pageSize}
              onChange={(event) => patch({ pageSize: Number(event.target.value) })}
            >
              <option value={10}>10</option>
              <option value={20}>20</option>
              <option value={50}>50</option>
            </select>
          </Field>

          <Button type="submit" variant="primary">
            Buscar
          </Button>
          <Button
            type="button"
            variant="ghost"
            onClick={() => {
              setDraft('')
              setFilters(emptyFilters)
            }}
          >
            Limpiar
          </Button>
        </form>
      </Card>

      <Card
        title={`Resultados (${formatNumber(invoices.data?.totalCount ?? 0)} facturas)`}
        subtitle={`Pagina ${filters.page} de ${formatNumber(invoices.data?.totalPages ?? 1)}`}
        actions={
          <div className="flex items-center gap-2">
            <Button
              disabled={!invoices.data?.hasPreviousPage}
              onClick={() => patch({ page: Math.max(1, filters.page - 1) })}
            >
              Anterior
            </Button>
            <Button
              disabled={!invoices.data?.hasNextPage}
              onClick={() => patch({ page: filters.page + 1 })}
            >
              Siguiente
            </Button>
          </div>
        }
      >
        {invoices.isLoading && <Loading />}
        {invoices.error ? <ErrorNote error={invoices.error} /> : null}
        {invoices.data && invoices.data.items.length === 0 && (
          <p className="px-1 py-6 text-center text-xs text-muted">
            Sin facturas para los filtros indicados.
          </p>
        )}
        {invoices.data && invoices.data.items.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full text-xs">
              <thead className="text-2xs uppercase tracking-wide text-muted">
                <tr>
                  <th className="py-1 text-left">N°</th>
                  <th className="py-1 text-left">Cliente</th>
                  <th className="py-1 text-right">Emision</th>
                  <th className="py-1 text-right">Vencimiento</th>
                  <th className="py-1 text-left">Estado</th>
                  <th className="py-1 text-left">Cobro</th>
                  <th className="py-1 text-right">Total</th>
                  <th className="py-1 text-right">Saldo</th>
                  <th className="py-1 text-right">Accion</th>
                </tr>
              </thead>
              <tbody>
                {invoices.data.items.map((invoice) => (
                  <tr key={invoice.id} className="border-t border-line hover:bg-raised">
                    <td className="tabular py-1.5 font-medium">#{invoice.number}</td>
                    <td className="py-1.5">
                      <span className="flex flex-col">
                        <span className="font-medium">{invoice.customerName}</span>
                        <span className="text-2xs text-muted">{invoice.customerRun}</span>
                      </span>
                    </td>
                    <td className="tabular py-1.5 text-right">{formatDate(invoice.invoiceDate)}</td>
                    <td className="tabular py-1.5 text-right">{formatDate(invoice.paymentDueDate)}</td>
                    <td className="py-1.5">
                      <Badge tone={statusTone(invoice.invoiceStatus)}>
                        {statusLabel(invoice.invoiceStatus)}
                      </Badge>
                    </td>
                    <td className="py-1.5">
                      <Badge tone={statusTone(invoice.paymentStatus)}>
                        {statusLabel(invoice.paymentStatus)}
                      </Badge>
                    </td>
                    <td className="tabular py-1.5 text-right">{formatMoney(invoice.totalAmount)}</td>
                    <td className="tabular py-1.5 text-right font-medium">
                      {formatMoney(invoice.outstandingBalance)}
                    </td>
                    <td className="py-1.5 text-right">
                      <Button onClick={() => setSelected(invoice.number)}>Detalle</Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {selected !== null && (
        <InvoiceDetailDrawer number={selected} token={token} onClose={() => setSelected(null)} />
      )}
    </div>
  )
}
