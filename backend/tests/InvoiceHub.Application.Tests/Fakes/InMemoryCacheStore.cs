using InvoiceHub.Application.Ports;

namespace InvoiceHub.Application.Tests.Fakes;

/// <summary>
/// Almacen de cache en memoria equivalente al adaptador de memoria de infraestructura, con instrumentacion
/// extra para verificar la politica cache-aside: cuantas escrituras se hicieron, que claves existen y que
/// prefijos se invalidaron tras una operacion de escritura.
/// </summary>
internal sealed class InMemoryCacheStore : ICacheStore
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly List<string> _invalidatedPrefixes = new();

    public DateTime UtcNow { get; set; } = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    public int GetCount { get; private set; }

    public int SetCount { get; private set; }

    public IReadOnlyList<string> InvalidatedPrefixes => _invalidatedPrefixes;

    public IReadOnlyCollection<string> Keys => _entries.Keys;

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        GetCount++;

        if (_entries.TryGetValue(key, out var entry) && entry.ExpiresAtUtc > UtcNow)
        {
            return Task.FromResult((T?)entry.Value);
        }

        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        SetCount++;
        _entries[key] = new Entry(value!, UtcNow.Add(timeToLive));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        _entries.Remove(key);
        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        _invalidatedPrefixes.Add(prefix);

        foreach (var key in _entries.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            _entries.Remove(key);
        }

        return Task.CompletedTask;
    }

    public Task<bool> PingAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public bool Contains(string key) => _entries.ContainsKey(key);

    /// <summary>Precarga una entrada como si otra consulta la hubiera dejado en cache.</summary>
    public void Seed<T>(string key, T value, TimeSpan? timeToLive = null)
        => _entries[key] = new Entry(value!, UtcNow.Add(timeToLive ?? TimeSpan.FromMinutes(5)));

    private sealed record Entry(object Value, DateTime ExpiresAtUtc);
}
