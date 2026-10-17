using InvoiceHub.Application.Ports;

namespace InvoiceHub.Application.Common;

/// <summary>
/// Politica cache-aside reutilizada por las consultas de lectura. Centraliza el tiempo de vida y la
/// invalidacion, de modo que cualquier escritura (importacion, nota de credito, pago o recalculo)
/// descarte tanto las busquedas como los reportes y el panel.
/// </summary>
public static class CacheAside
{
    /// <summary>Tiempo de vida de las respuestas de lectura. Corto para acotar el riesgo de datos obsoletos.</summary>
    public static readonly TimeSpan ReadThroughTtl = TimeSpan.FromSeconds(30);

    public static async Task<T> GetOrCreateAsync<T>(
        this ICacheStore cache,
        string key,
        Func<Task<T>> factory,
        CancellationToken cancellationToken,
        TimeSpan? timeToLive = null)
    {
        var cached = await cache.GetAsync<T>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var value = await factory();
        if (value is not null)
        {
            await cache.SetAsync(key, value, timeToLive ?? ReadThroughTtl, cancellationToken);
        }

        return value;
    }

    /// <summary>Descarta todas las entradas de lectura derivadas del estado de facturacion.</summary>
    public static async Task InvalidateInvoiceReadModelsAsync(this ICacheStore cache, CancellationToken cancellationToken)
    {
        await cache.RemoveByPrefixAsync(CacheKeys.InvoicePrefix, cancellationToken);
        await cache.RemoveByPrefixAsync(CacheKeys.ReportPrefix, cancellationToken);
        await cache.RemoveByPrefixAsync(CacheKeys.DashboardPrefix, cancellationToken);
    }
}
