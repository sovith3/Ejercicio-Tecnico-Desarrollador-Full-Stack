using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.ReadModels;

namespace InvoiceHub.Application.Mapping;

/// <summary>Traduce las filas de los reportes SQL a los contratos publicos de la API.</summary>
public static class ReportDtoMapper
{
    public static PaymentStatusSummaryItemDto ToItem(PaymentStatusSummaryRow row) => new(
        row.PaymentStatus.ToString(),
        InvoiceDtoMapper.PaymentStatusLabel(row.PaymentStatus),
        row.InvoiceCount,
        Common.Money.FromUnits(row.TotalAmountUnits),
        Math.Round(row.AmountPercentage, 2),
        Math.Round(row.CountPercentage, 2));

    public static PaymentStatusSummaryDto ToSummary(
        IReadOnlyList<PaymentStatusSummaryRow> rows,
        DateOnly generatedOn)
    {
        var items = rows.Select(ToItem).ToList();
        return new PaymentStatusSummaryDto(
            generatedOn,
            items.Sum(item => item.InvoiceCount),
            items.Sum(item => item.TotalAmount),
            items);
    }

    public static OverdueWithoutActivityItemDto ToItem(OverdueWithoutActivityRow row) => new(
        row.Number,
        row.InvoiceDate,
        row.PaymentDueDate,
        row.DaysOverdue,
        Common.Money.FromUnits(row.TotalAmountUnits),
        InvoiceDtoMapper.InvoiceStatusLabel(row.InvoiceStatus),
        row.CustomerRun,
        row.CustomerName,
        row.CustomerEmail);

    public static OverdueWithoutActivityReportDto ToReport(
        IReadOnlyList<OverdueWithoutActivityRow> rows,
        int minDaysOverdue,
        DateOnly generatedOn)
    {
        var items = rows.Select(ToItem).ToList();
        return new OverdueWithoutActivityReportDto(
            generatedOn,
            minDaysOverdue,
            items.Count,
            items.Sum(item => item.TotalAmount),
            items);
    }

    public static InconsistentInvoiceItemDto ToItem(InconsistentInvoiceRow row) => new(
        row.Number,
        row.InvoiceDate,
        Common.Money.FromUnits(row.DeclaredTotalUnits),
        Common.Money.FromUnits(row.DetailsSubtotalUnits),
        Common.Money.FromUnits(row.DifferenceUnits),
        row.DetailLineCount,
        row.CustomerRun,
        row.CustomerName,
        row.InconsistencyReason);

    public static InconsistentInvoicesReportDto ToReport(
        IReadOnlyList<InconsistentInvoiceRow> rows,
        DateOnly generatedOn)
    {
        var items = rows.Select(ToItem).ToList();
        return new InconsistentInvoicesReportDto(
            generatedOn,
            items.Count,
            items.Sum(item => item.DeclaredTotal),
            items.Sum(item => item.DetailsSubtotal),
            items);
    }

    public static InvoiceStatusBreakdownDto ToBreakdown(InvoiceStatusCountRow row) => new(
        row.InvoiceStatus.ToString(),
        InvoiceDtoMapper.InvoiceStatusLabel(row.InvoiceStatus),
        row.InvoiceCount,
        Common.Money.FromUnits(row.TotalAmountUnits));

    public static MonthlyBillingDto ToMonthly(MonthlyBillingRow row) => new(
        row.Period,
        row.InvoiceCount,
        Common.Money.FromUnits(row.TotalAmountUnits),
        Common.Money.FromUnits(row.CreditNotesUnits));

    public static TopDebtorDto ToTopDebtor(TopDebtorRow row) => new(
        row.CustomerRun,
        row.CustomerName,
        row.OverdueCount,
        Common.Money.FromUnits(row.OutstandingUnits));

    /// <summary>
    /// Compone la instantanea del panel. Los indicadores provienen de una sola consulta agregada; los
    /// resumenes de los reportes descargables se derivan de las mismas filas que alimentan esos reportes
    /// para que las cifras del panel y de los archivos nunca difieran.
    /// </summary>
    public static DashboardDto ToDashboard(
        DashboardSnapshot snapshot,
        IReadOnlyList<OverdueWithoutActivityRow> overdueWithoutActivity,
        IReadOnlyList<InconsistentInvoiceRow> inconsistentInvoices,
        DateOnly generatedOn) => new(
        generatedOn,
        snapshot.TotalInvoices,
        snapshot.ActiveInvoices,
        snapshot.InconsistentInvoices,
        Common.Money.FromUnits(snapshot.TotalBilledUnits),
        Common.Money.FromUnits(snapshot.ActiveBilledUnits),
        Common.Money.FromUnits(snapshot.OutstandingUnits),
        Common.Money.FromUnits(snapshot.CreditNotesUnits),
        snapshot.OverdueCount,
        Common.Money.FromUnits(snapshot.OverdueAmountUnits),
        snapshot.DueSoonCount,
        Common.Money.FromUnits(snapshot.DueSoonAmountUnits),
        snapshot.ByInvoiceStatus.Select(ToBreakdown).ToList(),
        snapshot.ByPaymentStatus.Select(ToItem).ToList(),
        snapshot.MonthlyBilling.Select(ToMonthly).ToList(),
        snapshot.TopDebtors.Select(ToTopDebtor).ToList(),
        new DownloadableReportSummaryDto(
            overdueWithoutActivity.Count,
            overdueWithoutActivity.Sum(row => Common.Money.FromUnits(row.TotalAmountUnits)),
            inconsistentInvoices.Count,
            inconsistentInvoices.Sum(row => Common.Money.FromUnits(row.DeclaredTotalUnits))));
}
