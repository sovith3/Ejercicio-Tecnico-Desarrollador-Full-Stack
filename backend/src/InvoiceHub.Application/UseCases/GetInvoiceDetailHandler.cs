using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.Mapping;
using InvoiceHub.Application.Ports;

namespace InvoiceHub.Application.UseCases;

/// <summary>
/// Caso de uso: obtener el detalle de una factura. El agregado se materializa completo (cliente,
/// lineas, notas de credito y pago) para exponer tanto los estados calculados como los declarados en
/// el origen de datos, lo que permite evidenciar las divergencias.
/// </summary>
public sealed class GetInvoiceDetailHandler
{
    private readonly IInvoiceRepository _invoices;
    private readonly ICacheStore _cache;
    private readonly IClock _clock;

    public GetInvoiceDetailHandler(IInvoiceRepository invoices, ICacheStore cache, IClock clock)
    {
        _invoices = invoices;
        _cache = cache;
        _clock = clock;
    }

    public async Task<InvoiceDetailDto> HandleAsync(int number, CancellationToken cancellationToken)
    {
        var cacheKey = CacheKeys.InvoiceDetail(number);

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async () =>
            {
                var invoice = await _invoices.GetByNumberAsync(number, cancellationToken)
                    ?? throw NotFoundException.ForInvoice(number);

                // Se refresca el estado en memoria con la fecha de negocio vigente para que la respuesta
                // no dependa de cuando se calculo por ultima vez (los pagos y las fechas cambian el estado).
                invoice.RefreshStatuses(_clock.Today);

                return InvoiceDtoMapper.ToDetail(invoice, _clock.Today);
            },
            cancellationToken);
    }
}
