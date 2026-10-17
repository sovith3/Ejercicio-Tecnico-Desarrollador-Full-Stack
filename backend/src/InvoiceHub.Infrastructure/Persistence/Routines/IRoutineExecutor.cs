namespace InvoiceHub.Infrastructure.Persistence.Routines;

/// <summary>
/// Sesion de ejecucion de rutinas: mantiene viva la conexion y la transaccion para que un adaptador
/// pueda componer varias sentencias SQL como una unica unidad atomica (por ejemplo: preparar el lote,
/// combinarlo, leer el resultado y limpiar).
/// </summary>
public interface IRoutineSession
{
    /// <summary>Indica si el adaptador descarto la transaccion; en ese caso el ejecutor no confirma.</summary>
    bool IsRolledBack { get; }

    /// <summary>Ejecuta el script indicado y devuelve la cantidad de filas afectadas.</summary>
    Task<int> ExecuteAsync(string script, object? parameters, CancellationToken cancellationToken);

    /// <summary>Ejecuta el script indicado y devuelve su primer valor escalar.</summary>
    Task<T?> ExecuteScalarAsync<T>(string script, object? parameters, CancellationToken cancellationToken);

    /// <summary>Ejecuta el script indicado y materializa su conjunto de resultados.</summary>
    Task<IReadOnlyList<T>> QueryAsync<T>(string script, object? parameters, CancellationToken cancellationToken);

    /// <summary>Descarta la transaccion en curso (la operacion se rechazo sin dejar cambios).</summary>
    Task RollbackAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Punto unico de acceso al motor de base de datos para las rutinas de calculo, carga y escritura.
/// </summary>
/// <remarks>
/// Es la frontera entre el almacenamiento y los adaptadores: resuelve el script en el catalogo,
/// abre la conexion con la misma cadena que usa EF Core y administra la transaccion inmediata
/// (<c>BEGIN IMMEDIATE</c>) cuando la operacion debe validar y escribir sin ventanas de carrera. Los
/// adaptadores de cada puerto eligen el script y traducen el resultado, sin conocer la conexion.
/// </remarks>
public interface IRoutineExecutor
{
    /// <summary>Ejecuta un unico script fuera de una transaccion explicita.</summary>
    Task<int> ExecuteAsync(string script, object? parameters, CancellationToken cancellationToken);

    /// <summary>Ejecuta un unico script escalar fuera de una transaccion explicita.</summary>
    Task<T?> ExecuteScalarAsync<T>(string script, object? parameters, CancellationToken cancellationToken);

    /// <summary>
    /// Ejecuta el trabajo indicado dentro de una transaccion inmediata: si el delegado termina sin
    /// revertir, la transaccion se confirma; si lanza o revierte, no queda ningun cambio aplicado.
    /// </summary>
    Task<TResult> ExecuteInImmediateTransactionAsync<TResult>(
        Func<IRoutineSession, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken);
}
