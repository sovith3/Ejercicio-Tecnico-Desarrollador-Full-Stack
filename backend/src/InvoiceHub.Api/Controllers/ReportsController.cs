using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.UseCases;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceHub.Api.Controllers;

/// <summary>
/// Reportes. Todas las consultas se resuelven con agregaciones dentro de SQLite (vistas y rutinas
/// almacenadas) y se sirven desde la cache durante su vigencia.
/// </summary>
[ApiController]
[Route("api/reports")]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
public sealed class ReportsController : ControllerBase
{
    private readonly GetPaymentStatusSummaryReportHandler _paymentStatusSummary;
    private readonly GetOverdueWithoutActivityReportHandler _overdueWithoutActivity;
    private readonly GetInconsistentInvoicesReportHandler _inconsistentInvoices;

    public ReportsController(
        GetPaymentStatusSummaryReportHandler paymentStatusSummary,
        GetOverdueWithoutActivityReportHandler overdueWithoutActivity,
        GetInconsistentInvoicesReportHandler inconsistentInvoices)
    {
        _paymentStatusSummary = paymentStatusSummary;
        _overdueWithoutActivity = overdueWithoutActivity;
        _inconsistentInvoices = inconsistentInvoices;
    }

    /// <summary>Reporte 2: totales y porcentajes de facturas por estado de cobro.</summary>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    [HttpGet("payment-status-summary")]
    [ProducesResponseType(typeof(PaymentStatusSummaryDto), StatusCodes.Status200OK)]
    public Task<PaymentStatusSummaryDto> GetPaymentStatusSummaryAsync(CancellationToken cancellationToken)
        => _paymentStatusSummary.HandleAsync(cancellationToken);

    /// <summary>
    /// Reporte 1: facturas consistentes, vencidas hace mas de <paramref name="minDaysOverdue"/> dias y
    /// sin pago registrado ni notas de credito.
    /// </summary>
    /// <param name="minDaysOverdue">Dias de mora minimos (1 a 3650).</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    [HttpGet("overdue-without-activity")]
    [ProducesResponseType(typeof(OverdueWithoutActivityReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public Task<OverdueWithoutActivityReportDto> GetOverdueWithoutActivityAsync(
        [FromQuery] int minDaysOverdue = 30,
        CancellationToken cancellationToken = default)
        => _overdueWithoutActivity.HandleAsync(minDaysOverdue, cancellationToken);

    /// <summary>Reporte 3: facturas cuyo total declarado difiere de la suma de los subtotales.</summary>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    [HttpGet("inconsistent-invoices")]
    [ProducesResponseType(typeof(InconsistentInvoicesReportDto), StatusCodes.Status200OK)]
    public Task<InconsistentInvoicesReportDto> GetInconsistentInvoicesAsync(CancellationToken cancellationToken)
        => _inconsistentInvoices.HandleAsync(cancellationToken);
}
