using Dapper;
using InvoiceHub.Application.Common;
using InvoiceHub.Application.Ports;
using InvoiceHub.Application.ReadModels;
using Microsoft.Extensions.Logging;

namespace InvoiceHub.Infrastructure.Persistence.Readers;

/// <summary>
/// Adaptador de reportes. Cada consulta es una agregacion resuelta por el motor sobre
/// <c>vw_invoice_search</c> (porcentajes con funciones de ventana, sumas del detalle y series
/// mensuales), y el panel principal devuelve sus cinco conjuntos en una sola ida y vuelta.
/// </summary>
public sealed class InvoiceReportReader : IInvoiceReportReader
{
    private readonly SqliteConnectionFactory _connections;
    private readonly SqlScriptCatalog _scripts;
    private readonly IClock _clock;
    private readonly ILogger<InvoiceReportReader> _logger;

    public InvoiceReportReader(
        SqliteConnectionFactory connections,
        SqlScriptCatalog scripts,
        IClock clock,
        ILogger<InvoiceReportReader> logger)
    {
        _connections = connections;
        _scripts = scripts;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PaymentStatusSummaryRow>> GetPaymentStatusSummaryAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<PaymentStatusSummaryRow>(
            new CommandDefinition(
                _scripts.Read(SqlScriptCatalog.PaymentStatusSummary),
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }

    public async Task<IReadOnlyList<OverdueWithoutActivityRow>> GetOverdueWithoutActivityAsync(
        int minDaysOverdue,
        CancellationToken cancellationToken)
    {
        // El umbral se acota a los limites del dominio antes de llegar al motor: la consulta recibe
        // siempre un entero util y el reporte nunca queda sin resultados por un valor absurdo.
        var days = Math.Clamp(
            minDaysOverdue, ReportThresholds.MinOverdueDays, ReportThresholds.MaxOverdueDays);

        await using var connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<OverdueWithoutActivityRow>(
            new CommandDefinition(
                _scripts.Read(SqlScriptCatalog.OverdueWithoutActivity),
                new { Today = SqliteValueFormat.ToStorage(_clock.Today), MinDaysOverdue = days },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }

    public async Task<IReadOnlyList<InconsistentInvoiceRow>> GetInconsistentInvoicesAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<InconsistentInvoiceRow>(
            new CommandDefinition(
                _scripts.Read(SqlScriptCatalog.InconsistentInvoices),
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }

    /// <summary>
    /// Ejecuta la consulta 33 y arma la instantanea del panel. Los cinco conjuntos se leen en orden fijo
    /// (indicadores, estados de factura, resumen de cobro, serie mensual y deudores) sobre un unico
    /// lector, de modo que el panel se resuelve con una sola ida y vuelta a la base de datos.
    /// </summary>
    public async Task<DashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken)
    {
        var parameters = new
        {
            Today = SqliteValueFormat.ToStorage(_clock.Today),
            DueSoonDays = ReportThresholds.DueSoonDays,
            MonthlyBillingMonths = ReportThresholds.MonthlyBillingMonths,
            TopDebtors = ReportThresholds.TopDebtors
        };

        await using var connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
                _scripts.Read(SqlScriptCatalog.Dashboard),
                parameters,
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        var indicators = await grid.ReadFirstOrDefaultAsync<DashboardSnapshot>().ConfigureAwait(false)
                         ?? new DashboardSnapshot();

        var byInvoiceStatus = (await grid.ReadAsync<InvoiceStatusCountRow>().ConfigureAwait(false)).AsList();
        var byPaymentStatus = (await grid.ReadAsync<PaymentStatusSummaryRow>().ConfigureAwait(false)).AsList();
        var monthlyBilling = (await grid.ReadAsync<MonthlyBillingRow>().ConfigureAwait(false)).AsList();
        var topDebtors = (await grid.ReadAsync<TopDebtorRow>().ConfigureAwait(false)).AsList();

        _logger.LogDebug(
            "Panel calculado: {Total} facturas, {Active} activas, {Overdue} vencidas.",
            indicators.TotalInvoices, indicators.ActiveInvoices, indicators.OverdueCount);

        return new DashboardSnapshot
        {
            TotalInvoices = indicators.TotalInvoices,
            ActiveInvoices = indicators.ActiveInvoices,
            InconsistentInvoices = indicators.InconsistentInvoices,
            TotalBilledUnits = indicators.TotalBilledUnits,
            ActiveBilledUnits = indicators.ActiveBilledUnits,
            OutstandingUnits = indicators.OutstandingUnits,
            CreditNotesUnits = indicators.CreditNotesUnits,
            OverdueCount = indicators.OverdueCount,
            OverdueAmountUnits = indicators.OverdueAmountUnits,
            DueSoonCount = indicators.DueSoonCount,
            DueSoonAmountUnits = indicators.DueSoonAmountUnits,
            ByInvoiceStatus = byInvoiceStatus,
            ByPaymentStatus = byPaymentStatus,
            MonthlyBilling = monthlyBilling,
            TopDebtors = topDebtors
        };
    }
}
