using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.Mapping;
using InvoiceHub.Application.Ports;

namespace InvoiceHub.Application.UseCases;

/// <summary>
/// Caso de uso: indicadores consolidados del panel principal. Se resuelve con una consulta agregada
/// (mas las dos consultas de los reportes descargables) y se sirve desde cache durante 30 segundos,
/// porque es la vista con mayor concurrencia de la aplicacion.
/// </summary>
public sealed class GetDashboardHandler
{
    private readonly IInvoiceReportReader _reader;
    private readonly ICacheStore _cache;
    private readonly IClock _clock;

    public GetDashboardHandler(IInvoiceReportReader reader, ICacheStore cache, IClock clock)
    {
        _reader = reader;
        _cache = cache;
        _clock = clock;
    }

    public async Task<DashboardDto> HandleAsync(int minDaysOverdue, CancellationToken cancellationToken)
    {
        if (!ReportThresholds.IsValidOverdueMinDays(minDaysOverdue))
        {
            throw new ValidationFailedException(
                nameof(minDaysOverdue),
                $"Los dias de mora deben estar entre {ReportThresholds.MinOverdueDays} y {ReportThresholds.MaxOverdueDays}.");
        }

        var generatedOn = _clock.Today;

        return await _cache.GetOrCreateAsync(
            $"{CacheKeys.Dashboard()}:overdue{minDaysOverdue}",
            async () =>
            {
                var snapshot = await _reader.GetDashboardAsync(cancellationToken);
                var overdue = await _reader.GetOverdueWithoutActivityAsync(minDaysOverdue, cancellationToken);
                var inconsistent = await _reader.GetInconsistentInvoicesAsync(cancellationToken);
                return ReportDtoMapper.ToDashboard(snapshot, overdue, inconsistent, generatedOn);
            },
            cancellationToken);
    }
}
