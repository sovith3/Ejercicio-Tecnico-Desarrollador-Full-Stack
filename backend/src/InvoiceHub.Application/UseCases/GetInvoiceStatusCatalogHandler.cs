using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.Mapping;
using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Enums;

namespace InvoiceHub.Application.UseCases;

/// <summary>Resultado del catalogo de estados disponibles.</summary>
public sealed record InvoiceStatusCatalogDto(
    DateOnly BusinessDate,
    IReadOnlyList<StatusOptionDto<InvoiceStatus>> InvoiceStatuses,
    IReadOnlyList<StatusOptionDto<PaymentStatus>> PaymentStatuses);

/// <summary>
/// Caso de uso: catalogo de estados con la cantidad de facturas y el monto de cada uno. Alimenta los
/// filtros y las tarjetas resumen de la interfaz sin necesidad de descargar el universo de facturas.
/// </summary>
public sealed class GetInvoiceStatusCatalogHandler
{
    private readonly IInvoiceSearchReader _searchReader;
    private readonly ICacheStore _cache;
    private readonly IClock _clock;

    public GetInvoiceStatusCatalogHandler(
        IInvoiceSearchReader searchReader,
        ICacheStore cache,
        IClock clock)
    {
        _searchReader = searchReader;
        _cache = cache;
        _clock = clock;
    }

    public async Task<InvoiceStatusCatalogDto> HandleAsync(
        bool onlyActiveSystem,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"{CacheKeys.InvoicePrefix}status-catalog:{onlyActiveSystem}";

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async () =>
            {
                var invoiceStatuses = await _searchReader.CountByInvoiceStatusAsync(onlyActiveSystem, cancellationToken);
                var paymentStatuses = await _searchReader.CountByPaymentStatusAsync(onlyActiveSystem, cancellationToken);

                var invoiceOptions = Enum.GetValues<InvoiceStatus>()
                    .Select(status =>
                    {
                        var row = invoiceStatuses.FirstOrDefault(item => item.InvoiceStatus == status);
                        return new StatusOptionDto<InvoiceStatus>(
                            status,
                            InvoiceDtoMapper.InvoiceStatusLabel(status),
                            row?.InvoiceCount ?? 0,
                            Money.FromUnits(row?.TotalAmountUnits ?? 0L));
                    })
                    .ToList();

                var paymentOptions = Enum.GetValues<PaymentStatus>()
                    .Select(status =>
                    {
                        var row = paymentStatuses.FirstOrDefault(item => item.PaymentStatus == status);
                        return new StatusOptionDto<PaymentStatus>(
                            status,
                            InvoiceDtoMapper.PaymentStatusLabel(status),
                            row?.InvoiceCount ?? 0,
                            Money.FromUnits(row?.TotalAmountUnits ?? 0L));
                    })
                    .ToList();

                return new InvoiceStatusCatalogDto(_clock.Today, invoiceOptions, paymentOptions);
            },
            cancellationToken);
    }
}
