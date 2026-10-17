using InvoiceHub.Application.ReadModels;

namespace InvoiceHub.Application.Ports;

/// <summary>
/// Puerto de lectura de reportes. Cada consulta se resuelve con agregaciones SQL (vistas y rutinas
/// almacenadas) para no materializar el universo de facturas en memoria.
/// </summary>
public interface IInvoiceReportReader
{
    /// <summary>Resumen de totales y porcentajes de facturas por estado de cobro.</summary>
    Task<IReadOnlyList<PaymentStatusSummaryRow>> GetPaymentStatusSummaryAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Facturas consistentes, con mas de <paramref name="minDaysOverdue"/> dias de vencimiento, sin pago
    /// registrado y sin notas de credito.
    /// </summary>
    Task<IReadOnlyList<OverdueWithoutActivityRow>> GetOverdueWithoutActivityAsync(
        int minDaysOverdue,
        CancellationToken cancellationToken);

    /// <summary>Facturas cuyo total declarado no coincide con la suma de los subtotales de sus productos.</summary>
    Task<IReadOnlyList<InconsistentInvoiceRow>> GetInconsistentInvoicesAsync(CancellationToken cancellationToken);

    /// <summary>Indicadores consolidados para el panel principal.</summary>
    Task<DashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken);
}
