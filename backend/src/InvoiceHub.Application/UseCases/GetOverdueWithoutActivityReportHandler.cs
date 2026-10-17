using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.Mapping;
using InvoiceHub.Application.Ports;

namespace InvoiceHub.Application.UseCases;

/// <summary>
/// Caso de uso: reporte de facturas consistentes, vencidas hace mas de N dias, sin pago registrado y
/// sin notas de credito (reporte 1). Es el insumo tipico de la gestion de cobranza.
/// </summary>
public sealed class GetOverdueWithoutActivityReportHandler
{
    private readonly IInvoiceReportReader _reader;
    private readonly ICacheStore _cache;
    private readonly IClock _clock;

    public GetOverdueWithoutActivityReportHandler(
        IInvoiceReportReader reader,
        ICacheStore cache,
        IClock clock)
    {
        _reader = reader;
        _cache = cache;
        _clock = clock;
    }

    public async Task<OverdueWithoutActivityReportDto> HandleAsync(
        int minDaysOverdue,
        CancellationToken cancellationToken)
    {
        if (!ReportThresholds.IsValidOverdueMinDays(minDaysOverdue))
        {
            throw new ValidationFailedException(
                nameof(minDaysOverdue),
                $"Los dias de mora deben estar entre {ReportThresholds.MinOverdueDays} y {ReportThresholds.MaxOverdueDays}.");
        }

        var generatedOn = _clock.Today;

        return await _cache.GetOrCreateAsync(
            CacheKeys.OverdueWithoutActivity(minDaysOverdue),
            async () =>
            {
                var rows = await _reader.GetOverdueWithoutActivityAsync(minDaysOverdue, cancellationToken);
                return ReportDtoMapper.ToReport(rows, minDaysOverdue, generatedOn);
            },
            cancellationToken);
    }
}
