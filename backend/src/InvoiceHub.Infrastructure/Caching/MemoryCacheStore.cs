using System.Collections.Concurrent;
using InvoiceHub.Application.Ports;

namespace InvoiceHub.Infrastructure.Caching;

/// <summary>
/// Cache en memoria con expiracion por entrada. Cumple dos funciones: es el adaptador usado cuando no
/// hay Redis configurado (desarrollo y pruebas) y es el respaldo al que se degrada
/// <see cref="RedisCacheStore"/> cuando el servidor de cache no responde, de modo que la aplicacion
/// nunca deje de servir una consulta por una dependencia opcional.
/// </summary>
public sealed class MemoryCacheStore : ICacheStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>Cantidad de entradas vigentes; se expone para diagnostico y pruebas.</summary>
    public int Count => _entries.Count(entry => entry.Value.ExpiresAtUtc > DateTime.UtcNow);

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAtUtc > DateTime.UtcNow && entry.Payload is T typed)
            {
                return Task.FromResult<T?>(typed);
            }

            _entries.TryRemove(key, out _);
        }

        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        if (value is null)
        {
            return Task.CompletedTask;
        }

        var effectiveTtl = timeToLive <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : timeToLive;
        _entries[key] = new Entry(value, DateTime.UtcNow.Add(effectiveTtl));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        foreach (var key in _entries.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                _entries.TryRemove(key, out _);
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> PingAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    private sealed record Entry(object Payload, DateTime ExpiresAtUtc);
}
