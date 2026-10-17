using InvoiceHub.Domain.Enums;

namespace InvoiceHub.Application.ReadModels;

/// <summary>Fila del resumen de facturas por estado de cobro (reporte 2).</summary>
public sealed class PaymentStatusSummaryRow
{
    public PaymentStatus PaymentStatus { get; init; }

    public int InvoiceCount { get; init; }

    /// <summary>Monto total facturado en el estado (unidades de 1/100000 de peso).</summary>
    public long TotalAmountUnits { get; init; }

    /// <summary>Porcentaje sobre el total facturado, calculado con funcion de ventana en SQL.</summary>
    public double AmountPercentage { get; init; }

    /// <summary>Porcentaje sobre la cantidad total de facturas.</summary>
    public double CountPercentage { get; init; }
}

/// <summary>Fila del reporte de facturas vencidas sin pago ni nota de credito (reporte 1).</summary>
public sealed class OverdueWithoutActivityRow
{
    public int Number { get; init; }

    public DateOnly InvoiceDate { get; init; }

    public DateOnly PaymentDueDate { get; init; }

    public int DaysOverdue { get; init; }

    public long TotalAmountUnits { get; init; }

    public string CustomerRun { get; init; } = string.Empty;

    public string CustomerName { get; init; } = string.Empty;

    public string CustomerEmail { get; init; } = string.Empty;

    public InvoiceStatus InvoiceStatus { get; init; }
}

/// <summary>Fila del reporte de facturas inconsistentes (reporte 3).</summary>
public sealed class InconsistentInvoiceRow
{
    public int Number { get; init; }

    public DateOnly InvoiceDate { get; init; }

    public long DeclaredTotalUnits { get; init; }

    public long DetailsSubtotalUnits { get; init; }

    public int DetailLineCount { get; init; }

    public string CustomerRun { get; init; } = string.Empty;

    public string CustomerName { get; init; } = string.Empty;

    public string? InconsistencyReason { get; init; }

    /// <summary>Diferencia entre el total declarado y la suma de subtotales (unidades de 1/100000 de peso).</summary>
    public long DifferenceUnits => DeclaredTotalUnits - DetailsSubtotalUnits;
}

/// <summary>Serie mensual de facturacion para el panel principal.</summary>
public sealed class MonthlyBillingRow
{
    /// <summary>Periodo en formato <c>yyyy-MM</c>.</summary>
    public string Period { get; init; } = string.Empty;

    public int InvoiceCount { get; init; }

    public long TotalAmountUnits { get; init; }

    public long CreditNotesUnits { get; init; }
}

/// <summary>Cliente con mayor saldo pendiente en facturas vencidas.</summary>
public sealed class TopDebtorRow
{
    public string CustomerRun { get; init; } = string.Empty;

    public string CustomerName { get; init; } = string.Empty;

    public int OverdueCount { get; init; }

    public long OutstandingUnits { get; init; }
}

/// <summary>Instantanea consolidada usada por el panel principal del frontend.</summary>
public sealed class DashboardSnapshot
{
    public int TotalInvoices { get; init; }

    public int ActiveInvoices { get; init; }

    public int InconsistentInvoices { get; init; }

    public long TotalBilledUnits { get; init; }

    public long ActiveBilledUnits { get; init; }

    public long OutstandingUnits { get; init; }

    public long CreditNotesUnits { get; init; }

    public int OverdueCount { get; init; }

    public long OverdueAmountUnits { get; init; }

    public int DueSoonCount { get; init; }

    public long DueSoonAmountUnits { get; init; }

    public IReadOnlyList<InvoiceStatusCountRow> ByInvoiceStatus { get; init; } = Array.Empty<InvoiceStatusCountRow>();

    public IReadOnlyList<PaymentStatusSummaryRow> ByPaymentStatus { get; init; } =
        Array.Empty<PaymentStatusSummaryRow>();

    public IReadOnlyList<MonthlyBillingRow> MonthlyBilling { get; init; } = Array.Empty<MonthlyBillingRow>();

    public IReadOnlyList<TopDebtorRow> TopDebtors { get; init; } = Array.Empty<TopDebtorRow>();
}
