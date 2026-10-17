import { useDashboard } from '../api/hooks'
import { Card, EmptyState, ErrorNote, Loading, Stat } from '../components/ui'
import { formatMoney, formatNumber, formatPercent, statusLabel } from '../format'

/**
 * Panel de control: indicadores consolidados y desglose por estado. Las etiquetas viajan traducidas
 * desde la API junto al valor del dominio.
 */
export default function DashboardPanel() {
  const dashboard = useDashboard(30)

  if (dashboard.isLoading) {
    return <Loading label="Consultando los indicadores del panel…" />
  }

  if (dashboard.error) {
    return <ErrorNote error={dashboard.error} />
  }

  const data = dashboard.data

  if (!data) {
    return <EmptyState label="La API no devolvio indicadores." />
  }

  const pendingRate = data.activeBilled > 0 ? (data.outstanding / data.activeBilled) * 100 : 0

  return (
    <div className="flex flex-col gap-4">
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Stat
          label="Facturado"
          value={formatMoney(data.totalBilled)}
          hint={`${formatNumber(data.totalInvoices)} facturas almacenadas`}
        />
        <Stat
          label="Facturado vigente"
          value={formatMoney(data.activeBilled)}
          hint={`${formatNumber(data.activeInvoices)} facturas en el sistema activo`}
        />
        <Stat
          label="Saldo por cobrar"
          value={formatMoney(data.outstanding)}
          hint={`${formatPercent(pendingRate)} del monto vigente`}
          tone="warn"
        />
        <Stat
          label="Vencido"
          value={formatMoney(data.overdueAmount)}
          hint={`${formatNumber(data.overdueCount)} facturas vencidas`}
          tone="bad"
        />
        <Stat
          label="Notas de credito"
          value={formatMoney(data.creditNotesTotal)}
          hint="Total emitido sobre el sistema activo"
        />
        <Stat
          label="Inconsistencias"
          value={formatNumber(data.inconsistentInvoices)}
          hint="Fuera de busqueda, catalogo y cartera"
          tone="bad"
        />
        <Stat
          label="Por vencer (30 dias)"
          value={formatMoney(data.dueSoonAmount)}
          hint={`${formatNumber(data.dueSoonCount)} facturas`}
          tone="good"
        />
        <Stat label="Fecha de negocio" value={data.generatedOn} hint="Zona horaria America/Santiago" />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="Facturacion mensual" subtitle="Monto emitido y notas de credito por periodo">
          {data.monthlyBilling.length === 0 ? (
            <EmptyState label="Sin periodos facturados." />
          ) : (
            <table className="w-full text-xs">
              <thead className="text-2xs uppercase tracking-wide text-muted">
                <tr>
                  <th className="py-1 text-left">Periodo</th>
                  <th className="py-1 text-right">Facturas</th>
                  <th className="py-1 text-right">Monto</th>
                  <th className="py-1 text-right">Notas</th>
                </tr>
              </thead>
              <tbody>
                {data.monthlyBilling.map((row) => (
                  <tr key={row.period} className="border-t border-line">
                    <td className="py-1.5 font-medium">{row.period}</td>
                    <td className="tabular py-1.5 text-right">{formatNumber(row.invoiceCount)}</td>
                    <td className="tabular py-1.5 text-right">{formatMoney(row.totalAmount)}</td>
                    <td className="tabular py-1.5 text-right text-muted">
                      {formatMoney(row.creditNotesTotal)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Card>

        <Card title="Mayores deudores" subtitle="Saldo por cobrar acumulado">
          {data.topDebtors.length === 0 ? (
            <EmptyState label="Sin saldos pendientes." />
          ) : (
            <ul className="flex flex-col gap-2 text-xs">
              {data.topDebtors.map((debtor) => (
                <li
                  key={debtor.customerRun}
                  className="flex items-center justify-between gap-3 border-b border-line pb-1.5 last:border-0"
                >
                  <span className="flex flex-col">
                    <span className="font-medium">{debtor.customerName}</span>
                    <span className="text-2xs text-muted">
                      {debtor.customerRun} · {formatNumber(debtor.overdueCount)} vencidas
                    </span>
                  </span>
                  <span className="tabular font-semibold">{formatMoney(debtor.outstanding)}</span>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="Facturas por estado" subtitle="Sistema activo, sin inconsistencias">
          <StatusTable
            rows={data.byInvoiceStatus.map((item) => ({
              key: String(item.status ?? item.value ?? ''),
              label: item.statusLabel ?? item.label ?? statusLabel(item.status ?? item.value),
              count: item.invoiceCount,
              amount: item.totalAmount,
              percentage: item.amountPercentage
            }))}
          />
        </Card>

        <Card title="Estado de cobro" subtitle="Distribucion del monto vigente">
          <StatusTable
            rows={data.byPaymentStatus.map((item) => ({
              key: String(item.status ?? item.value ?? ''),
              label: item.statusLabel ?? item.label ?? statusLabel(item.status ?? item.value),
              count: item.invoiceCount,
              amount: item.totalAmount,
              percentage: item.amountPercentage
            }))}
          />
        </Card>
      </div>
    </div>
  )
}

interface StatusRow {
  key: string
  label: string
  count: number
  amount: number
  percentage?: number
}

function StatusTable({ rows }: { rows: StatusRow[] }) {
  if (rows.length === 0) {
    return <EmptyState label="Sin datos para el periodo." />
  }

  return (
    <table className="w-full text-xs">
      <thead className="text-2xs uppercase tracking-wide text-muted">
        <tr>
          <th className="py-1 text-left">Estado</th>
          <th className="py-1 text-right">Facturas</th>
          <th className="py-1 text-right">Monto</th>
          <th className="py-1 text-right">% monto</th>
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => (
          <tr key={row.key} className="border-t border-line">
            <td className="py-1.5 font-medium">{row.label}</td>
            <td className="tabular py-1.5 text-right">{formatNumber(row.count)}</td>
            <td className="tabular py-1.5 text-right">{formatMoney(row.amount)}</td>
            <td className="tabular py-1.5 text-right text-muted">
              {row.percentage === undefined ? '—' : formatPercent(row.percentage)}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
