using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.Mapping;
using InvoiceHub.Application.Ports;

namespace InvoiceHub.Application.UseCases;

/// <summary>
/// Caso de uso: buscar facturas con filtros, ordenamiento y paginacion. El filtrado, el orden y el
/// conteo total se resuelven en el motor de base de datos; aqui solo se normalizan los criterios, se
/// traduce el resultado y se aplica cache de lectura.
/// </summary>
public sealed class SearchInvoicesHandler
{
    private readonly IInvoiceSearchReader _searchReader;
    private readonly ICacheStore _cache;

    public SearchInvoicesHandler(IInvoiceSearchReader searchReader, ICacheStore cache)
    {
        _searchReader = searchReader;
        _cache = cache;
    }

    public async Task<PagedResult<InvoiceListItemDto>> HandleAsync(
        InvoiceSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        InvoiceSearchCriteriaNormalizer.Normalize(criteria);

        var cacheKey = CacheKeys.InvoiceSearch(
            InvoiceSearchCriteriaNormalizer.Fingerprint(criteria),
            criteria.Page,
            criteria.PageSize);

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async () =>
            {
                var page = await _searchReader.SearchAsync(criteria, cancellationToken);
                var items = page.Items.Select(InvoiceDtoMapper.ToListItem).ToList();
                return new PagedResult<InvoiceListItemDto>(items, page.Page, page.PageSize, page.TotalCount);
            },
            cancellationToken);
    }
}
