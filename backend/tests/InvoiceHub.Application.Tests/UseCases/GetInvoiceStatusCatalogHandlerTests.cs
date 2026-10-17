using InvoiceHub.Application.Common;
using InvoiceHub.Application.Tests.Fakes;
using InvoiceHub.Application.UseCases;
using InvoiceHub.Domain.Enums;

namespace InvoiceHub.Application.Tests.UseCases;

/// <summary>
/// Pruebas del catalogo de estados. El contrato debe incluir siempre las tres opciones de cada estado
/// (aunque no existan facturas en ese estado) con etiquetas en espanol y montos en unidades monetarias.
/// </summary>
public sealed class GetInvoiceStatusCatalogHandlerTests
{
    private readonly FakeInvoiceSearchReader _searchReader = new();
    private readonly InMemoryCacheStore _cache = new();
    private readonly FixedClock _clock = FixedClock.Default();

    private GetInvoiceStatusCatalogHandler CreateHandler()
        => new(_searchReader, _cache, _clock);

    [Fact]
    public async Task HandleAsync_ReturnsEveryStatusWithCountsAndAmounts()
    {
        _searchReader.InvoiceStatusCounts.Add(new()
        {
            InvoiceStatus = InvoiceStatus.Partial,
            InvoiceCount = 7,
            TotalAmountUnits = 1_500_000_000
        });

        _searchReader.PaymentStatusCounts.Add(new()
        {
            PaymentStatus = PaymentStatus.Overdue,
            InvoiceCount = 3,
            TotalAmountUnits = 750_000_000
        });

        var catalog = await CreateHandler().HandleAsync(onlyActiveSystem: true, CancellationToken.None);

        Assert.Equal(_clock.Today, catalog.BusinessDate);
        Assert.Equal(3, catalog.InvoiceStatuses.Count);
        Assert.Equal(3, catalog.PaymentStatuses.Count);

        var issued = catalog.InvoiceStatuses.Single(option => option.Value == InvoiceStatus.Issued);
        Assert.Equal("Emitida", issued.Label);
        Assert.Equal(0, issued.InvoiceCount);
        Assert.Equal(0m, issued.TotalAmount);

        var partial = catalog.InvoiceStatuses.Single(option => option.Value == InvoiceStatus.Partial);
        Assert.Equal("Parcial", partial.Label);
        Assert.Equal(7, partial.InvoiceCount);
        Assert.Equal(15_000m, partial.TotalAmount);

        var cancelled = catalog.InvoiceStatuses.Single(option => option.Value == InvoiceStatus.Cancelled);
        Assert.Equal("Anulada", cancelled.Label);

        var overdue = catalog.PaymentStatuses.Single(option => option.Value == PaymentStatus.Overdue);
        Assert.Equal("Vencida", overdue.Label);
        Assert.Equal(3, overdue.InvoiceCount);
        Assert.Equal(7_500m, overdue.TotalAmount);

        Assert.Equal("Pendiente", catalog.PaymentStatuses.Single(o => o.Value == PaymentStatus.Pending).Label);
        Assert.Equal("Pagada", catalog.PaymentStatuses.Single(o => o.Value == PaymentStatus.Paid).Label);
        Assert.True(_searchReader.LastOnlyConsistent);
    }

    [Fact]
    public async Task HandleAsync_ForwardsOnlyActiveSystemFlagToTheReader()
    {
        var catalog = await CreateHandler().HandleAsync(onlyActiveSystem: false, CancellationToken.None);

        Assert.False(_searchReader.LastOnlyConsistent);
        Assert.Equal(_clock.Today, catalog.BusinessDate);
    }

    [Fact]
    public async Task HandleAsync_SecondCallIsServedFromCache()
    {
        var handler = CreateHandler();

        await handler.HandleAsync(onlyActiveSystem: true, CancellationToken.None);
        await handler.HandleAsync(onlyActiveSystem: true, CancellationToken.None);

        Assert.True(_cache.Contains($"{CacheKeys.InvoicePrefix}status-catalog:{true}"));

        await handler.HandleAsync(onlyActiveSystem: false, CancellationToken.None);

        Assert.True(_cache.Contains($"{CacheKeys.InvoicePrefix}status-catalog:{false}"));
    }
}
