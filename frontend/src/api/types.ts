/** Contratos de la API InvoiceHub tal como los publica Swagger (enums como texto, camelCase). */

export type InvoiceStatus = 'Issued' | 'Partial' | 'Cancelled'

export type PaymentStatus = 'Pending' | 'Overdue' | 'Paid'

/** Fila de la busqueda paginada (`GET /api/invoices`). */
export interface InvoiceListItem {
  id: number
  number: number
  invoiceDate: string
  paymentDueDate: string
  daysToDue: number
  daysOverdue: number
  customerRun: string
  customerName: string
  customerEmail: string
  totalAmount: number
  creditNotesTotal: number
  outstandingBalance: number
  creditNoteCount: number
  invoiceStatus: InvoiceStatus
  paymentStatus: PaymentStatus
  isConsistent: boolean
  hasPayment: boolean
}

export interface InvoiceSearchResponse {
  items: InvoiceListItem[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasNextPage: boolean
  hasPreviousPage: boolean
}

export interface InvoiceDetailLine {
  productName: string
  unitPrice: number
  quantity: number
  subtotal: number
  expectedSubtotal: number
  hasInconsistentSubtotal: boolean
}

export interface CreditNote {
  id: number
  /** Numero correlativo de la nota de credito (no es el numero de factura). */
  number: number
  creditNoteDate: string
  amount: number
  createdAtUtc: string
}

export interface InvoicePayment {
  paymentMethod: string
  paymentDate: string
  recordedAtUtc: string
}

/** Detalle completo (`GET /api/invoices/{number}`), con los estados declarados para auditoria. */
export interface InvoiceDetail {
  id: number
  number: number
  invoiceDate: string
  paymentDueDate: string
  daysToDue: number
  daysOverdue: number
  customerRun: string
  customerName: string
  customerEmail: string
  totalAmount: number
  detailsSubtotalSum: number
  creditNotesTotal: number
  outstandingBalance: number
  invoiceStatus: InvoiceStatus
  paymentStatus: PaymentStatus
  isConsistent: boolean
  hasInconsistentDetailLines: boolean
  declaredStatus: InvoiceStatus
  declaredPaymentStatus: PaymentStatus
  isDeclaredStatusDivergent: boolean
  isDeclaredPaymentStatusDivergent: boolean
  details: InvoiceDetailLine[]
  creditNotes: CreditNote[]
  payment?: InvoicePayment | null
  createdAtUtc: string
  updatedAtUtc: string
}

export interface StatusCount {
  status?: InvoiceStatus | PaymentStatus
  value?: InvoiceStatus | PaymentStatus
  statusLabel?: string
  label?: string
  invoiceCount: number
  totalAmount: number
  amountPercentage?: number
  countPercentage?: number
}

export interface MonthlyBilling {
  period: string
  invoiceCount: number
  totalAmount: number
  creditNotesTotal: number
}

export interface TopDebtor {
  customerRun: string
  customerName: string
  overdueCount: number
  outstanding: number
}

/** `GET /api/dashboard`. */
export interface DashboardResponse {
  generatedOn: string
  totalInvoices: number
  activeInvoices: number
  inconsistentInvoices: number
  totalBilled: number
  activeBilled: number
  outstanding: number
  creditNotesTotal: number
  overdueCount: number
  overdueAmount: number
  dueSoonCount: number
  dueSoonAmount: number
  byInvoiceStatus: StatusCount[]
  byPaymentStatus: StatusCount[]
  monthlyBilling: MonthlyBilling[]
  topDebtors: TopDebtor[]
}

export interface StatusCatalogResponse {
  businessDate: string
  invoiceStatuses: StatusCount[]
  paymentStatuses: StatusCount[]
}

/** `GET /api/reports/payment-status-summary` (reporte 2). */
export interface PaymentStatusSummaryResponse {
  generatedOn: string
  totalInvoices: number
  totalAmount: number
  items: StatusCount[]
}

export interface OverdueInvoice {
  number: number
  invoiceDate: string
  paymentDueDate: string
  daysOverdue: number
  totalAmount: number
  invoiceStatus: string
  customerRun: string
  customerName: string
  customerEmail: string
}

/** `GET /api/reports/overdue-without-activity` (reporte 1). */
export interface OverdueWithoutActivityResponse {
  generatedOn: string
  minDaysOverdue: number
  totalInvoices: number
  totalAmount: number
  items: OverdueInvoice[]
}

export interface InconsistentInvoice {
  number: number
  invoiceDate: string
  declaredTotal: number
  detailsSubtotal: number
  difference: number
  detailLineCount: number
  customerRun: string
  customerName: string
  reason: string
}

/** `GET /api/reports/inconsistent-invoices` (reporte 3). */
export interface InconsistentInvoicesResponse {
  generatedOn: string
  totalInvoices: number
  totalDeclared: number
  totalDetailsSubtotal: number
  items: InconsistentInvoice[]
}

export interface LoginResponse {
  accessToken: string
  tokenType: string
  expiresAtUtc: string
  user: {
    username: string
    displayName: string
    role: string
  }
}

export interface HealthCheckEntry {
  name: string
  status: string
  description?: string | null
  durationMilliseconds: number
  data?: Record<string, string | null>
}

export interface HealthReport {
  status: string
  checkedAtUtc: string
  totalDurationMilliseconds: number
  checks: HealthCheckEntry[]
}
