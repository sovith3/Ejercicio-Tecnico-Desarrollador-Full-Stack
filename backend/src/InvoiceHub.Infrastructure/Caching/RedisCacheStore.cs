using System.Net.Sockets;
using System.Text.Json;
using InvoiceHub.Application.Ports;
using InvoiceHub.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace InvoiceHub.Infrastructure.Caching;

/// <summary>
/// Adaptador de cache sobre Redis con degradacion automatica a memoria.
/// </summary>
/// <remarks>
/// La cache es una dependencia <b>opcional</b> para la disponibilidad: si el servidor no esta disponible
/// (o no responde dentro del tiempo de espera) cada operacion se resuelve contra
/// <see cref="MemoryCacheStore"/>, de modo que una consulta nunca falla por la cache y la invalidacion
/// sigue siendo efectiva en el proceso actual. Los valores se serializan a JSON para no acoplar el
/// contrato de las respuestas al formato interno de Redis.
/// </remarks>
public sealed class RedisCacheStore : ICacheStore
{
    private const int DeleteBatchSize = 500;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly MemoryCacheStore _fallback;
    private readonly CacheOptions _options;
    private readonly ILogger<RedisCacheStore> _logger;
    private readonly IConnectionMultiplexer? _connection;

    public RedisCacheStore(CacheOptions options, MemoryCacheStore fallback, ILogger<RedisCacheStore> logger)
    {
        _options = options;
        _fallback = fallback;
        _logger = logger;

        try
        {
            var configuration = options.ResolveConfiguration();

            _connection = ConnectionMultiplexer.Connect(configuration);
            _logger.LogInformation(
                "Cache Redis configurada en {Endpoint} (instancia '{Instance}', contrasena {Password}).",
                options.DescribeEndpoint(),
                string.IsNullOrEmpty(options.InstanceName) ? "sin prefijo" : options.InstanceName,
                options.HasPassword ? "configurada" : "ausente");
        }
        catch (Exception exception) when (exception is RedisConnectionException
                                              or SocketException
                                              or TimeoutException
                                              or ArgumentException
                                              or InvalidOperationException)
        {
            _logger.LogWarning(
                exception,
                "No se pudo configurar Redis en {Endpoint}; las consultas usaran cache en memoria.",
                options.DescribeEndpoint());

            _connection = null;
        }
    }

    /// <summary>Indica si el adaptador tiene una conexion Redis configurada (diagnostico).</summary>
    public bool IsRedisConfigured => _connection is not null;

    /// <summary>Indica si la conexion con el servidor esta disponible ahora mismo (salud y diagnostico).</summary>
    public bool IsConnected => _connection?.IsConnected == true;

    /// <summary>Indica si se configuro contrasena; el valor nunca se expone fuera del adaptador.</summary>
    public bool PasswordConfigured => _options.HasPassword;

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        var database = TryGetDatabase();
        if (database is null)
        {
            return await _fallback.GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var payload = await database.StringGetAsync(Compose(key)).ConfigureAwait(false);
            if (payload.IsNullOrEmpty)
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(payload!, SerializerOptions);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(
                exception,
                "La entrada de cache {Key} no pudo deserializarse; se descarta y se recalcula.",
                key);

            await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return default;
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            _logger.LogWarning(exception, "Lectura de cache degradada a memoria para la clave {Key}.", key);
            return await _fallback.GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        if (value is null)
        {
            return;
        }

        await _fallback.SetAsync(key, value, timeToLive, cancellationToken).ConfigureAwait(false);

        var database = TryGetDatabase();
        if (database is null)
        {
            return;
        }

        try
        {
            var payload = JsonSerializer.Serialize(value, SerializerOptions);
            var effectiveTtl = timeToLive <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : timeToLive;
            await database.StringSetAsync(Compose(key), payload, effectiveTtl).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            _logger.LogWarning(exception, "Escritura de cache degradada a memoria para la clave {Key}.", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        await _fallback.RemoveAsync(key, cancellationToken).ConfigureAwait(false);

        var database = TryGetDatabase();
        if (database is null)
        {
            return;
        }

        try
        {
            await database.KeyDeleteAsync(Compose(key)).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            _logger.LogWarning(exception, "No se pudo eliminar la clave {Key} de Redis.", key);
        }
    }

    /// <summary>
    /// Invalida por prefijo recorriendo las claves del servidor (Redis no tiene borrado por patron).
    /// Se ejecuta tras cada escritura de facturacion para que busquedas, reportes y panel no sirvan
    /// datos obsoletos; un fallo aqui solo degrada a la cache en memoria, nunca la operacion de negocio.
    /// </summary>
    public async Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        await _fallback.RemoveByPrefixAsync(prefix, cancellationToken).ConfigureAwait(false);

        var connection = _connection;
        if (connection is null || !connection.IsConnected)
        {
            return;
        }

        try
        {
            var database = connection.GetDatabase();
            var pattern = Compose(prefix) + "*";
            var deleted = 0;

            foreach (var endpoint in connection.GetEndPoints())
            {
                var server = connection.GetServer(endpoint);
                if (!server.IsConnected || server.IsReplica)
                {
                    continue;
                }

                foreach (var key in server.Keys(pattern: pattern, pageSize: DeleteBatchSize))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await database.KeyDeleteAsync(key).ConfigureAwait(false);
                    deleted++;
                }
            }

            if (deleted > 0)
            {
                _logger.LogDebug(
                    "Invalidacion de cache: {Count} entradas eliminadas con el prefijo {Prefix}.",
                    deleted,
                    prefix);
            }
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            _logger.LogWarning(exception, "No se pudieron invalidar las entradas con prefijo {Prefix}.", prefix);
        }
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken)
    {
        var database = TryGetDatabase();
        if (database is null)
        {
            return await _fallback.PingAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await database.PingAsync().ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            _logger.LogWarning(exception, "El servidor de cache no respondio al ping; se usa cache en memoria.");
            return false;
        }
    }

    /// <summary>Prefijo efectivo de las claves cuando se comparte la instancia de Redis.</summary>
    private string Compose(string key) => string.Concat(_options.InstanceName, key);

    /// <summary>
    /// Devuelve la base de datos de Redis o <c>null</c> cuando no hay conexion utilizable, para que el
    /// llamador resuelva contra la cache en memoria.
    /// </summary>
    private IDatabase? TryGetDatabase()
    {
        var connection = _connection;
        if (connection is null)
        {
            return null;
        }

        try
        {
            if (!connection.IsConnected)
            {
                _logger.LogDebug("Redis no esta conectado; se usa cache en memoria.");
                return null;
            }

            return connection.GetDatabase();
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            _logger.LogWarning(exception, "Redis no esta disponible; se usa cache en memoria.");
            return null;
        }
    }

    /// <summary>Fallos propios de una dependencia externa que deben degradar en lugar de propagarse.</summary>
    private static bool IsTransient(Exception exception) =>
        exception is RedisException
            or TimeoutException
            or SocketException
            or ObjectDisposedException
            or InvalidOperationException;
}
