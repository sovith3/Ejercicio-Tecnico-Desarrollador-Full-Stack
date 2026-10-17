namespace InvoiceHub.Application.Common;

/// <summary>
/// Nomenclatura unica de las claves de cache. Centralizarlas permite que la invalidacion posterior a
/// una escritura (importacion, nota de credito, recalculo) sea consistente y no deje datos obsoletos.
/// </summary>
public static class CacheKeys
{
    public const string InvoicePrefix = "invoicehub:invoices:";
    public const string ReportPrefix = "invoicehub:reports:";
    public const string DashboardPrefix = "invoicehub:dashboard:";

    /// <summary>Version del esquema de las respuestas cacheadas; cambiarla descarta entradas antiguas.</summary>
    private const string Version = "v1";

    public static string InvoiceSearch(int criteriaHash, int page, int pageSize)
        => $"{InvoicePrefix}search:{Version}:{criteriaHash}:p{page}:s{pageSize}";

    public static string InvoiceDetail(int number) => $"{InvoicePrefix}detail:{Version}:{number}";

    public static string PaymentStatusSummary() => $"{ReportPrefix}payment-status-summary:{Version}";

    public static string OverdueWithoutActivity(int minDaysOverdue)
        => $"{ReportPrefix}overdue-without-activity:{Version}:{minDaysOverdue}";

    public static string InconsistentInvoices() => $"{ReportPrefix}inconsistent-invoices:{Version}";

    public static string Dashboard() => $"{DashboardPrefix}summary:{Version}";
}
