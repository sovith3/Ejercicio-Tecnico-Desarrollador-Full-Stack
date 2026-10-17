using InvoiceHub.Application.Common;
using InvoiceHub.Application.Tests.Fakes;

namespace InvoiceHub.Application.Tests.Common;

/// <summary>
/// Pruebas de la nomenclatura de claves y de la politica cache-aside. Se verifica el aislamiento entre
/// familias de claves (facturas, reportes y panel) porque la invalidacion posterior a una escritura se
/// apoya en sus prefijos.
/// </summary>
public sealed class CacheKeysAndCacheAsideTests
{
    private readonly InMemoryCacheStore _cache = new();

    [Fact]
    public void InvoiceSearch_KeyIncludesVersionPageAndPageSize()
    {
        var key = CacheKeys.InvoiceSearch(12345, page: 2, pageSize: 50);

        Assert.StartsWith(CacheKeys.InvoicePrefix, key);
        Assert.Contains("search:v1:12345:p2:s50", key);
    }

    [Fact]
    public void InvoiceDetail_KeyIsPerInvoiceNumber()
    {
        Assert.EndsWith("detail:v1:1000", CacheKeys.InvoiceDetail(1000));
        Assert.NotEqual(CacheKeys.InvoiceDetail(1000), CacheKeys.InvoiceDetail(1001));
        Assert.StartsWith(CacheKeys.InvoicePrefix, CacheKeys.InvoiceDetail(1000));
    }

    [Fact]
    public void ReportAndDashboardKeys_UseTheirOwnPrefixes()
    {
        Assert.StartsWith(CacheKeys.ReportPrefix, CacheKeys.PaymentStatusSummary());
        Assert.StartsWith(CacheKeys.ReportPrefix, CacheKeys.InconsistentInvoices());
        Assert.EndsWith("overdue-without-activity:v1:30", CacheKeys.OverdueWithoutActivity(30));
        Assert.StartsWith(CacheKeys.DashboardPrefix, CacheKeys.Dashboard());
    }

    [Fact]
    public void Prefixes_AreDisjoint()
    {
        Assert.False(CacheKeys.ReportPrefix.StartsWith(CacheKeys.InvoicePrefix, StringComparison.Ordinal));
        Assert.False(CacheKeys.DashboardPrefix.StartsWith(CacheKeys.InvoicePrefix, StringComparison.Ordinal));
        Assert.False(CacheKeys.DashboardPrefix.StartsWith(CacheKeys.ReportPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetOrCreateAsync_InvokesFactoryOnlyOnce()
    {
        var invocations = 0;

        async Task<string> Factory()
        {
            invocations++;
            await Task.Yield();
            return "valor";
        }

        var first = await _cache.GetOrCreateAsync("clave", Factory, CancellationToken.None);
        var second = await _cache.GetOrCreateAsync("clave", Factory, CancellationToken.None);

        Assert.Equal("valor", first);
        Assert.Equal("valor", second);
        Assert.Equal(1, invocations);
        Assert.Equal(1, _cache.SetCount);
    }

    [Fact]
    public async Task GetOrCreateAsync_UsesTheConfiguredTimeToLive()
    {
        await _cache.GetOrCreateAsync(
            "clave",
            () => Task.FromResult("valor"),
            CancellationToken.None,
            timeToLive: TimeSpan.FromMinutes(1));

        _cache.UtcNow = _cache.UtcNow.AddMinutes(2);

        var invocations = 0;
        var value = await _cache.GetOrCreateAsync(
            "clave",
            () =>
            {
                invocations++;
                return Task.FromResult("otro");
            },
            CancellationToken.None);

        Assert.Equal("otro", value);
        Assert.Equal(1, invocations);
    }

    [Fact]
    public async Task InvalidateInvoiceReadModelsAsync_RemovesEveryReadFamily()
    {
        _cache.Seed(CacheKeys.InvoiceSearch(1, 1, 25), "busqueda");
        _cache.Seed(CacheKeys.InvoiceDetail(1000), "detalle");
        _cache.Seed(CacheKeys.PaymentStatusSummary(), "reporte");
        _cache.Seed(CacheKeys.OverdueWithoutActivity(30), "reporte");
        _cache.Seed(CacheKeys.Dashboard(), "panel");
        _cache.Seed("otra:aplicacion:clave", "intacta");

        await _cache.InvalidateInvoiceReadModelsAsync(CancellationToken.None);

        Assert.Equal(
            new[] { CacheKeys.InvoicePrefix, CacheKeys.ReportPrefix, CacheKeys.DashboardPrefix },
            _cache.InvalidatedPrefixes);
        Assert.Equal(new[] { "otra:aplicacion:clave" }, _cache.Keys);
    }

    [Fact]
    public async Task RemoveAsync_RemovesOnlyTheRequestedKey()
    {
        _cache.Seed(CacheKeys.InvoiceDetail(1000), "detalle");
        _cache.Seed(CacheKeys.InvoiceDetail(2000), "detalle");

        await _cache.RemoveAsync(CacheKeys.InvoiceDetail(1000), CancellationToken.None);

        Assert.False(_cache.Contains(CacheKeys.InvoiceDetail(1000)));
        Assert.True(_cache.Contains(CacheKeys.InvoiceDetail(2000)));
    }
}
