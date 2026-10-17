using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.Tests.Fakes;
using InvoiceHub.Application.UseCases;
using InvoiceHub.Domain.Enums;

namespace InvoiceHub.Application.Tests.UseCases;

/// <summary>
/// Pruebas del caso de uso de busqueda. Verifican la normalizacion de criterios, la conversion de
/// unidades enteras a pesos, los metadatos de paginacion y que la cache de lectura separe paginas y
/// filtros distintos.
/// </summary>
public sealed class SearchInvoicesHandlerTests
{
    private readonly FakeInvoiceSearchReader _searchReader = new();
    private readonly InMemoryCacheStore _cache = new();

    private SearchInvoicesHandler CreateHandler() => new(_searchReader, _cache);

    [Fact]
    public async Task HandleAsync_MapsRowsToContractWithAmountsFromUnits()
    {
        _searchReader.Rows.Add(TestRows.Row(
            number: 1000,
            invoiceId: 10,
            totalAmountUnits: 200_000_000,
            creditNotesTotalUnits: 50_000_000,
            outstandingBalanceUnits: 150_000_000,
            creditNoteCount: 1,
            invoiceStatus: InvoiceStatus.Partial,
            paymentStatus: PaymentStatus.Overdue,
            daysOverdue: 12));

        var result = await CreateHandler().HandleAsync(new InvoiceSearchCriteria(), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(1000, item.Number);
        Assert.Equal(2000m, item.TotalAmount);
        Assert.Equal(500m, item.CreditNotesTotal);
        Assert.Equal(1500m, item.OutstandingBalance);
        Assert.Equal(1, item.CreditNoteCount);
        Assert.Equal(InvoiceStatus.Partial, item.InvoiceStatus);
        Assert.Equal(PaymentStatus.Overdue, item.PaymentStatus);
        Assert.Equal(12, item.DaysOverdue);
        Assert.False(item.HasPayment);
        Assert.Equal("Cliente Demo", item.CustomerName);
    }

    [Fact]
    public async Task HandleAsync_ReturnsPaginationMetadata()
    {
        foreach (var number in Enumerable.Range(1, 30))
        {
            _searchReader.Rows.Add(TestRows.Row(number: number, invoiceId: number));
        }

        var result = await CreateHandler().HandleAsync(
            new InvoiceSearchCriteria { Page = 1, PageSize = 25 },
            CancellationToken.None);

        Assert.Equal(25, result.Items.Count);
        Assert.Equal(30, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.True(result.HasNextPage);
    }

    [Fact]
    public async Task HandleAsync_NormalizesCriteriaBeforeQueryingTheEngine()
    {
        var criteria = new InvoiceSearchCriteria
        {
            Page = 0,
            PageSize = 0,
            Text = "  ACME  ",
            CustomerRun = " 12.345.678-9 ",
            SortBy = "campo-inexistente",
            SortDirection = "descendente",
            MinDaysOverdue = -5
        };

        await CreateHandler().HandleAsync(criteria, CancellationToken.None);

        Assert.Equal(1, criteria.Page);
        Assert.Equal(25, criteria.PageSize);
        Assert.Equal("acme", criteria.Text);
        Assert.Equal("12.345.678-9", criteria.CustomerRun);
        Assert.Equal("number", criteria.SortBy);
        Assert.Equal("desc", criteria.SortDirection);
        Assert.Null(criteria.MinDaysOverdue);
        Assert.Same(criteria, _searchReader.LastCriteria);
    }

    [Fact]
    public async Task HandleAsync_WithEmptyTextAndRun_ClearsThem()
    {
        var criteria = new InvoiceSearchCriteria { Text = "   ", CustomerRun = string.Empty };

        await CreateHandler().HandleAsync(criteria, CancellationToken.None);

        Assert.Null(criteria.Text);
        Assert.Null(criteria.CustomerRun);
    }

    [Fact]
    public async Task HandleAsync_RepeatedCriteria_IsServedFromCache()
    {
        _searchReader.Rows.Add(TestRows.Row());
        var handler = CreateHandler();

        var first = await handler.HandleAsync(new InvoiceSearchCriteria(), CancellationToken.None);
        var second = await handler.HandleAsync(new InvoiceSearchCriteria(), CancellationToken.None);

        Assert.Equal(1, _searchReader.SearchCallCount);
        Assert.Equal(1, _cache.SetCount);
        Assert.Equal(first.Items.Select(item => item.Number), second.Items.Select(item => item.Number));
    }

    [Fact]
    public async Task HandleAsync_DifferentPage_UsesDifferentCacheKey()
    {
        foreach (var number in Enumerable.Range(1, 60))
        {
            _searchReader.Rows.Add(TestRows.Row(number: number, invoiceId: number));
        }

        var handler = CreateHandler();

        await handler.HandleAsync(new InvoiceSearchCriteria { Page = 1, PageSize = 25 }, CancellationToken.None);
        await handler.HandleAsync(new InvoiceSearchCriteria { Page = 2, PageSize = 25 }, CancellationToken.None);

        Assert.Equal(2, _searchReader.SearchCallCount);

        var expectedKey = CacheKeys.InvoiceSearch(
            InvoiceSearchCriteriaNormalizer.Fingerprint(new InvoiceSearchCriteria { Page = 2, PageSize = 25 }),
            2,
            25);

        Assert.Contains(_cache.Keys, key => key == expectedKey);
    }

    [Fact]
    public async Task HandleAsync_DifferentFilter_UsesDifferentCacheKey()
    {
        _searchReader.Rows.Add(TestRows.Row());
        var handler = CreateHandler();

        await handler.HandleAsync(
            new InvoiceSearchCriteria { InvoiceStatus = InvoiceStatus.Issued },
            CancellationToken.None);
        await handler.HandleAsync(
            new InvoiceSearchCriteria { InvoiceStatus = InvoiceStatus.Partial },
            CancellationToken.None);

        Assert.Equal(2, _searchReader.SearchCallCount);
        Assert.Equal(2, _cache.SetCount);
    }

    [Fact]
    public async Task HandleAsync_UsesFreshDataAfterCacheInvalidation()
    {
        _searchReader.Rows.Add(TestRows.Row());
        var handler = CreateHandler();

        await handler.HandleAsync(new InvoiceSearchCriteria(), CancellationToken.None);

        _searchReader.Rows.Clear();
        _searchReader.Rows.Add(TestRows.Row(number: 2000, invoiceId: 20));

        await _cache.InvalidateInvoiceReadModelsAsync(CancellationToken.None);

        var result = await handler.HandleAsync(new InvoiceSearchCriteria(), CancellationToken.None);

        Assert.Equal(2, _searchReader.SearchCallCount);
        Assert.Equal(2000, Assert.Single(result.Items).Number);
    }

    [Fact]
    public async Task HandleAsync_TreatsExpiredEntryAsCacheMiss()
    {
        _searchReader.Rows.Add(TestRows.Row());
        var handler = CreateHandler();

        await handler.HandleAsync(new InvoiceSearchCriteria(), CancellationToken.None);

        // El TTL de lectura es corto (30 s): al avanzar el reloj del almacen la entrada deja de ser utilizable.
        _cache.UtcNow = _cache.UtcNow.Add(CacheAside.ReadThroughTtl).AddSeconds(1);

        await handler.HandleAsync(new InvoiceSearchCriteria(), CancellationToken.None);

        Assert.Equal(2, _searchReader.SearchCallCount);
    }

    [Fact]
    public async Task HandleAsync_WithNoRows_ReturnsEmptyPage()
    {
        var result = await CreateHandler().HandleAsync(new InvoiceSearchCriteria(), CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
        Assert.False(result.HasNextPage);
    }

    [Fact]
    public async Task HandleAsync_WithNullCriteria_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            CreateHandler().HandleAsync(null!, CancellationToken.None));
    }
}
