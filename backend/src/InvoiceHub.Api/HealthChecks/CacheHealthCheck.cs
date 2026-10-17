using InvoiceHub.Application.Ports;
using InvoiceHub.Infrastructure.Caching;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace InvoiceHub.Api.HealthChecks;

/// <summary>
/// Comprueba el almacen de cache efectivo. Si Redis no responde el adaptador degrada a memoria, de
/// modo que un estado degradado se informa como <c>Degraded</c> y no inutiliza el servicio.
/// </summary>
public sealed class CacheHealthCheck : IHealthCheck
{
    private readonly ICacheStore _cache;

    public CacheHealthCheck(ICacheStore cache) => _cache = cache;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var provider = _cache is RedisCacheStore ? "redis" : "memory";
        var data = new Dictionary<string, object>
        {
            ["provider"] = provider,
            ["implementation"] = _cache.GetType().Name
        };

        if (_cache is RedisCacheStore redisStore)
        {
            data["connected"] = redisStore.IsConnected;
            data["passwordConfigured"] = redisStore.PasswordConfigured;

            // Una contrasena equivocada (o un host/puerto incorrectos) no impide arrancar, pero si degrada:
            // el adaptador responde desde memoria. Se informa como Degraded para que la causa sea visible.
            if (!redisStore.IsConnected)
            {
                return HealthCheckResult.Degraded(
                    "Redis esta configurado pero no responde; revisa host, puerto y contrasena (AUTH). "
                    + "La aplicacion continua con el respaldo en memoria.",
                    data: data);
            }
        }

        var probeKey = $"health:probe:{Guid.NewGuid():N}";

        try
        {
            await _cache.SetAsync(probeKey, DateTime.UtcNow, TimeSpan.FromSeconds(30), cancellationToken)
                .ConfigureAwait(false);

            var stored = await _cache.GetAsync<DateTime>(probeKey, cancellationToken).ConfigureAwait(false);

            await _cache.RemoveAsync(probeKey, cancellationToken).ConfigureAwait(false);

            if (stored == default)
            {
                return HealthCheckResult.Degraded(
                    "El almacen de cache respondio pero no devolvio el valor escrito.",
                    data: data);
            }

            return HealthCheckResult.Healthy($"Cache operativa ({provider}).", data);
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Degraded(
                $"El almacen de cache no respondio; la aplicacion continua con el respaldo en memoria ({provider}).",
                exception,
                data);
        }
    }
}
