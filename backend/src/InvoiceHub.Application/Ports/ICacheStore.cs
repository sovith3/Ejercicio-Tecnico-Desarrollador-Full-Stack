namespace InvoiceHub.Application.Ports;

/// <summary>
/// Puerto de la base de datos de cache (Redis en produccion, memoria como respaldo). Se usa para
/// servir de forma rapida las consultas concurrentes mas frecuentes (busquedas, detalle y reportes).
/// </summary>
public interface ICacheStore
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken);

    Task SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken);

    Task RemoveAsync(string key, CancellationToken cancellationToken);

    /// <summary>Invalida todas las entradas cuyo nombre comience con el prefijo indicado.</summary>
    Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken);

    /// <summary>Comprueba la disponibilidad del almacen de cache (usado por el health check).</summary>
    Task<bool> PingAsync(CancellationToken cancellationToken);
}

/// <summary>Acceso al reloj del sistema para poder sustituirlo en pruebas.</summary>
public interface IClock
{
    /// <summary>Fecha/hora UTC actual.</summary>
    DateTime UtcNow { get; }

    /// <summary>Fecha de negocio vigente (zona horaria configurada) usada para vencimientos.</summary>
    DateOnly Today { get; }
}
