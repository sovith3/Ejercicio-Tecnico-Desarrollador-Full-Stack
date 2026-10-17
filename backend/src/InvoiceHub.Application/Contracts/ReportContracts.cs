namespace InvoiceHub.Application.Contracts;

/// <summary>Item del resumen de facturas por estado de cobro (reporte 2).</summary>
public sealed record PaymentStatusSummaryItemDto(
    string Status,
    string StatusLabel,
    int InvoiceCount,
    decimal TotalAmount,
    double AmountPercentage,
    double CountPercentage);

/// <summary>Reporte 2: totales y porcentajes de facturas por estado de cobro.</summary>
public sealed record PaymentStatusSummaryDto(
    DateOnly GeneratedOn,
    int TotalInvoices,
    decimal TotalAmount,
    IReadOnlyList<PaymentStatusSummaryItemDto> Items);

/// <summary>Item del reporte de facturas vencidas sin actividad (reporte 1).</summary>
public sealed record OverdueWithoutActivityItemDto(
    int Number,
    DateOnly InvoiceDate,
    DateOnly PaymentDueDate,
    int DaysOverdue,
    decimal TotalAmount,
    string InvoiceStatus,
    string CustomerRun,
    string CustomerName,
    string CustomerEmail);

/// <summary>Reporte 1: facturas consistentes, vencidas hace mas de N dias, sin pago ni nota de credito.</summary>
public sealed record OverdueWithoutActivityReportDto(
    DateOnly GeneratedOn,
    int MinDaysOverdue,
    int TotalInvoices,
    decimal TotalAmount,
    IReadOnlyList<OverdueWithoutActivityItemDto> Items);

/// <summary>Item del reporte de facturas inconsistentes (reporte 3).</summary>
public sealed record InconsistentInvoiceItemDto(
    int Number,
    DateOnly InvoiceDate,
    decimal DeclaredTotal,
    decimal DetailsSubtotal,
    decimal Difference,
    int DetailLineCount,
    string CustomerRun,
    string CustomerName,
    string? Reason);

/// <summary>Reporte 3: facturas cuyo total declarado difiere de la suma de subtotales de productos.</summary>
public sealed record InconsistentInvoicesReportDto(
    DateOnly GeneratedOn,
    int TotalInvoices,
    decimal TotalDeclared,
    decimal TotalDetailsSubtotal,
    IReadOnlyList<InconsistentInvoiceItemDto> Items);

/// <summary>Conteo por estado de factura.</summary>
public sealed record InvoiceStatusBreakdownDto(string Status, string StatusLabel, int InvoiceCount, decimal TotalAmount);

/// <summary>Serie mensual de facturacion.</summary>
public sealed record MonthlyBillingDto(
    string Period,
    int InvoiceCount,
    decimal TotalAmount,
    decimal CreditNotesTotal);

/// <summary>Cliente con mayor saldo vencido.</summary>
public sealed record TopDebtorDto(string CustomerRun, string CustomerName, int OverdueCount, decimal Outstanding);

/// <summary>Indicadores consolidados del panel principal.</summary>
public sealed record DashboardDto(
    DateOnly GeneratedOn,
    int TotalInvoices,
    int ActiveInvoices,
    int InconsistentInvoices,
    decimal TotalBilled,
    decimal ActiveBilled,
    decimal Outstanding,
    decimal CreditNotesTotal,
    int OverdueCount,
    decimal OverdueAmount,
    int DueSoonCount,
    decimal DueSoonAmount,
    IReadOnlyList<InvoiceStatusBreakdownDto> ByInvoiceStatus,
    IReadOnlyList<PaymentStatusSummaryItemDto> ByPaymentStatus,
    IReadOnlyList<MonthlyBillingDto> MonthlyBilling,
    IReadOnlyList<TopDebtorDto> TopDebtors,
    DownloadableReportSummaryDto Reports);

/// <summary>Resumen compacto de los reportes disponible en el panel.</summary>
public sealed record DownloadableReportSummaryDto(
    int OverdueWithoutActivityCount,
    decimal OverdueWithoutActivityAmount,
    int InconsistentCount,
    decimal InconsistentDeclaredAmount);
