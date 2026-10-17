using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.Mapping;
using InvoiceHub.Application.Ports;

namespace InvoiceHub.Application.UseCases;

/// <summary>
/// Caso de uso: reporte de facturas cuyo total declarado no coincide con la suma de los subtotales de
/// sus productos (reporte 3). Estas facturas se conservan en la base pero quedan fuera del sistema
/// activo hasta que se corrijan en el origen.
/// </summary>
public sealed class GetInconsistentInvoicesReportHandler
{
    private readonly IInvoiceReportReader _reader;
    private readonly ICacheStore _cache;
    private readonly IClock _clock;

    public GetInconsistentInvoicesReportHandler(
        IInvoiceReportReader reader,
        ICacheStore cache,
        IClock clock)
    {
        _reader = reader;
        _cache = cache;
        _clock = clock;
    }

    public async Task<InconsistentInvoicesReportDto> HandleAsync(CancellationToken cancellationToken)
    {
        var generatedOn = _clock.Today;

        return await _cache.GetOrCreateAsync(
            CacheKeys.InconsistentInvoices(),
            async () =>
            {
                var rows = await _reader.GetInconsistentInvoicesAsync(cancellationToken);
                return ReportDtoMapper.ToReport(rows, generatedOn);
            },
            cancellationToken);
    }
}
