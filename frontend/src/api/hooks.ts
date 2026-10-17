import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { query, request } from './client'
import type {
  DashboardResponse,
  HealthReport,
  InconsistentInvoicesResponse,
  InvoiceDetail,
  InvoiceSearchResponse,
  InvoiceStatus,
  LoginResponse,
  OverdueWithoutActivityResponse,
  PaymentStatus,
  PaymentStatusSummaryResponse,
  StatusCatalogResponse
} from './types'

export interface InvoiceSearchFilters {
  text?: string
  invoiceStatus?: InvoiceStatus | ''
  paymentStatus?: PaymentStatus | ''
  onlyActiveSystem?: boolean
  page: number
  pageSize: number
  sortBy: string
  sortDirection: 'asc' | 'desc'
}

export const queryKeys = {
  health: ['health'] as const,
  dashboard: (minDaysOverdue: number) => ['dashboard', minDaysOverdue] as const,
  statusCatalog: ['status-catalog'] as const,
  invoices: (filters: InvoiceSearchFilters) => ['invoices', filters] as const,
  invoice: (number: number) => ['invoice', number] as const,
  paymentSummary: ['reports', 'payment-status-summary'] as const,
  overdue: (minDaysOverdue: number) => ['reports', 'overdue-without-activity', minDaysOverdue] as const,
  inconsistent: ['reports', 'inconsistent-invoices'] as const,
  importSource: ['imports', 'source'] as const
}

/** Salud de la API: dato vivo, sin cache, porque resume el estado de sqlite, redis y el origen JSON. */
export const useHealth = () =>
  useQuery({
    queryKey: queryKeys.health,
    queryFn: () => request<HealthReport>('/health'),
    refetchInterval: 30_000
  })

export const useDashboard = (minDaysOverdue: number) =>
  useQuery({
    queryKey: queryKeys.dashboard(minDaysOverdue),
    queryFn: () => request<DashboardResponse>(`/dashboard${query({ minDaysOverdue })}`)
  })

export const useStatusCatalog = () =>
  useQuery({
    queryKey: queryKeys.statusCatalog,
    queryFn: () => request<StatusCatalogResponse>('/invoices/status-catalog')
  })

export const useInvoices = (filters: InvoiceSearchFilters) =>
  useQuery({
    queryKey: queryKeys.invoices(filters),
    queryFn: () =>
      request<InvoiceSearchResponse>(
        `/invoices${query({
          Text: filters.text,
          InvoiceStatus: filters.invoiceStatus,
          PaymentStatus: filters.paymentStatus,
          OnlyActiveSystem: filters.onlyActiveSystem ? true : undefined,
          SortBy: filters.sortBy,
          SortDirection: filters.sortDirection,
          Page: filters.page,
          PageSize: filters.pageSize
        })}`
      ),
    placeholderData: (previous) => previous
  })

export const useInvoiceDetail = (number: number | null) =>
  useQuery({
    queryKey: queryKeys.invoice(number ?? 0),
    queryFn: () => request<InvoiceDetail>(`/invoices/${number}`),
    enabled: number !== null && number > 0
  })

export const usePaymentStatusSummary = () =>
  useQuery({
    queryKey: queryKeys.paymentSummary,
    queryFn: () => request<PaymentStatusSummaryResponse>('/reports/payment-status-summary')
  })

export const useOverdueWithoutActivity = (minDaysOverdue: number) =>
  useQuery({
    queryKey: queryKeys.overdue(minDaysOverdue),
    queryFn: () =>
      request<OverdueWithoutActivityResponse>(
        `/reports/overdue-without-activity${query({ minDaysOverdue })}`
      )
  })

export const useInconsistentInvoices = () =>
  useQuery({
    queryKey: queryKeys.inconsistent,
    queryFn: () => request<InconsistentInvoicesResponse>('/reports/inconsistent-invoices')
  })

export interface ImportSourceResponse {
  filePath: string
  exists: boolean
  sizeInBytes: number
  modifiedAtUtc: string | null
  persistedInvoices: number
  lastRun: {
    id: number
    sourceFile: string
    startedAtUtc: string
    finishedAtUtc: string | null
    totalRecords: number
    importedRecords: number
    inconsistentRecords: number
    skippedRecords: number
    failedRecords: number
    status: string
    durationMilliseconds: number
    issues: string[]
  } | null
}

export const useImportSource = () =>
  useQuery({
    queryKey: queryKeys.importSource,
    queryFn: () => request<ImportSourceResponse>('/imports/source')
  })

/** Autenticacion: el token que devuelve la API habilita las operaciones de escritura. */
export const useLogin = () =>
  useMutation({
    mutationFn: (credentials: { username: string; password: string }) =>
      request<LoginResponse>('/auth/login', { method: 'POST', body: credentials })
  })

/**
 * Escrituras del panel. El token viaja en cada llamada, por lo que la sesion queda fuera de la cache de
 * React Query y, cuando una operacion cambia el estado, se invalidan las consultas afectadas.
 */
export const useCreditNoteWriter = (token: string | null) => {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (input: { number: number; amount: number; creditNoteDate?: string }) =>
      request<void>(`/invoices/${input.number}/credit-notes`, {
        method: 'POST',
        token,
        body: {
          number: input.number,
          amount: input.amount,
          creditNoteDate: input.creditNoteDate
        }
      }),
    onSuccess: (_result, input) => {
      void client.invalidateQueries({ queryKey: queryKeys.invoice(input.number) })
      void client.invalidateQueries({ queryKey: ['invoices'] })
      void client.invalidateQueries({ queryKey: ['dashboard'] })
      void client.invalidateQueries({ queryKey: ['reports'] })
    }
  })
}

export const usePaymentWriter = (token: string | null) => {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (input: { number: number; paymentMethod: string; paymentDate?: string }) =>
      request<void>(`/invoices/${input.number}/payments`, {
        method: 'POST',
        token,
        body: {
          paymentMethod: input.paymentMethod,
          paymentDate: input.paymentDate
        }
      }),
    onSuccess: (_result, input) => {
      void client.invalidateQueries({ queryKey: queryKeys.invoice(input.number) })
      void client.invalidateQueries({ queryKey: ['invoices'] })
      void client.invalidateQueries({ queryKey: ['dashboard'] })
      void client.invalidateQueries({ queryKey: ['reports'] })
    }
  })
}

export const useImportRunner = (token: string | null) => {
  const client = useQueryClient()

  return useMutation({
    mutationFn: () => request<unknown>('/imports', { method: 'POST', token }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['invoices'] })
      void client.invalidateQueries({ queryKey: ['dashboard'] })
      void client.invalidateQueries({ queryKey: ['reports'] })
      void client.invalidateQueries({ queryKey: queryKeys.importSource })
    }
  })
}

