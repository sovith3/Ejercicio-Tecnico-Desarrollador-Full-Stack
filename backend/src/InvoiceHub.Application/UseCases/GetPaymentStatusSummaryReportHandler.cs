using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.Mapping;
using InvoiceHub.Application.Ports;

namespace InvoiceHub.Application.UseCases;

/// <summary>
/// Caso de uso: reporte de totales y porcentajes de facturas por estado de cobro (reporte 2). Los
/// porcentajes se calculan con funciones de ventana en SQL para no traer el universo de facturas.
/// </summary>
public sealed class GetPaymentStatusSummaryReportHandler
{
    private readonly IInvoiceReportReader _reader;
    private readonly ICacheStore _cache;
    private readonly IClock _clock;

    public GetPaymentStatusSummaryReportHandler(
        IInvoiceReportReader reader,
        ICacheStore cache,
        IClock clock)
    {
        _reader = reader;
        _cache = cache;
        _clock = clock;
    }

    public async Task<PaymentStatusSummaryDto> HandleAsync(CancellationToken cancellationToken)
    {
        var generatedOn = _clock.Today;

        return await _cache.GetOrCreateAsync(
            CacheKeys.PaymentStatusSummary(),
            async () =>
            {
                var rows = await _reader.GetPaymentStatusSummaryAsync(cancellationToken);
                return ReportDtoMapper.ToSummary(rows, generatedOn);
            },
            cancellationToken);
    }
}
