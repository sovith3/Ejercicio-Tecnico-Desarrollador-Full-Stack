using InvoiceHub.Infrastructure.Caching;

namespace InvoiceHub.Infrastructure.Tests.Caching;

/// <summary>
/// Pruebas del almacen de cache en memoria: es el adaptador usado cuando no hay Redis y tambien el respaldo
/// al que degrada el adaptador de Redis, por lo que su contrato (expiracion, prefijos y tolerancia a nulos)
/// condiciona el comportamiento de la politica cache-aside.
/// </summary>
public sealed class MemoryCacheStoreTests
{
    private readonly MemoryCacheStore _cache = new();

    [Fact]
    public async Task SetAndGet_RoundTripsTheValue()
    {
        await _cache.SetAsync("clave", new Payload(42, "demo"), TimeSpan.FromMinutes(1), CancellationToken.None);

        var value = await _cache.GetAsync<Payload>("clave", CancellationToken.None);

        Assert.NotNull(value);
        Assert.Equal(42, value!.Number);
        Assert.Equal("demo", value.Text);
        Assert.Equal(1, _cache.Count);
    }

    [Fact]
    public async Task Get_WithUnknownKey_ReturnsDefault()
    {
        Assert.Null(await _cache.GetAsync<Payload>("inexistente", CancellationToken.None));
        Assert.Null(await _cache.GetAsync<string>("inexistente", CancellationToken.None));
    }

    [Fact]
    public async Task Set_WithNullValue_IsIgnored()
    {
        await _cache.SetAsync<Payload?>("clave", null, TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.Equal(0, _cache.Count);
    }

    [Fact]
    public async Task Get_WithExpiredEntry_ReturnsDefaultAndRemovesIt()
    {
        await _cache.SetAsync("clave", new Payload(1, "expirado"), TimeSpan.FromMilliseconds(30), CancellationToken.None);

        await Task.Delay(120);

        Assert.Null(await _cache.GetAsync<Payload>("clave", CancellationToken.None));
        Assert.Equal(0, _cache.Count);
    }

    [Fact]
    public async Task Set_WithNonPositiveTimeToLive_StillExpiresQuickly()
    {
        await _cache.SetAsync("clave", new Payload(1, "minimo"), TimeSpan.Zero, CancellationToken.None);

        Assert.NotNull(await _cache.GetAsync<Payload>("clave", CancellationToken.None));

        await Task.Delay(1200);

        Assert.Null(await _cache.GetAsync<Payload>("clave", CancellationToken.None));
    }

    [Fact]
    public async Task Get_WithDifferentTypeInTheSameKey_ReturnsDefault()
    {
        await _cache.SetAsync("clave", new Payload(1, "payload"), TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.Null(await _cache.GetAsync<string>("clave", CancellationToken.None));
    }

    [Fact]
    public async Task Remove_DeletesOnlyTheRequestedKey()
    {
        await _cache.SetAsync("a:1", "uno", TimeSpan.FromMinutes(1), CancellationToken.None);
        await _cache.SetAsync("b:1", "dos", TimeSpan.FromMinutes(1), CancellationToken.None);

        await _cache.RemoveAsync("a:1", CancellationToken.None);

        Assert.Null(await _cache.GetAsync<string>("a:1", CancellationToken.None));
        Assert.Equal("dos", await _cache.GetAsync<string>("b:1", CancellationToken.None));
    }

    [Fact]
    public async Task RemoveByPrefix_DeletesOnlyMatchingEntries()
    {
        await _cache.SetAsync("invoicehub:invoices:search:1", "busqueda", TimeSpan.FromMinutes(1), CancellationToken.None);
        await _cache.SetAsync("invoicehub:invoices:detail:1", "detalle", TimeSpan.FromMinutes(1), CancellationToken.None);
        await _cache.SetAsync("invoicehub:reports:summary", "reporte", TimeSpan.FromMinutes(1), CancellationToken.None);

        await _cache.RemoveByPrefixAsync("invoicehub:invoices:", CancellationToken.None);

        Assert.Null(await _cache.GetAsync<string>("invoicehub:invoices:search:1", CancellationToken.None));
        Assert.Null(await _cache.GetAsync<string>("invoicehub:invoices:detail:1", CancellationToken.None));
        Assert.Equal("reporte", await _cache.GetAsync<string>("invoicehub:reports:summary", CancellationToken.None));
        Assert.Equal(1, _cache.Count);
    }

    [Fact]
    public async Task RemoveByPrefix_WithUnusedPrefix_KeepsEverything()
    {
        await _cache.SetAsync("a", "valor", TimeSpan.FromMinutes(1), CancellationToken.None);

        await _cache.RemoveByPrefixAsync("zzz:", CancellationToken.None);

        Assert.Equal("valor", await _cache.GetAsync<string>("a", CancellationToken.None));
    }

    [Fact]
    public async Task Ping_IsAlwaysHealthy()
    {
        Assert.True(await _cache.PingAsync(CancellationToken.None));
    }

    private sealed record Payload(int Number, string Text);
}
