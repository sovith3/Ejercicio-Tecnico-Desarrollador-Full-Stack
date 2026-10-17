using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace InvoiceHub.Infrastructure.Persistence.Routines;

/// <summary>
/// Implementacion del punto unico de acceso al motor. Cada llamada abre su propia conexion (SQLite
/// serializa las escrituras) y, cuando la operacion lo exige, la envuelve en una transaccion inmediata
/// para que ninguna otra sesion se cuele entre la validacion y la escritura.
/// </summary>
public sealed class SqlRoutineExecutor : IRoutineExecutor
{
    private readonly SqliteConnectionFactory _connections;
    private readonly SqlScriptCatalog _scripts;
    private readonly ILogger<SqlRoutineExecutor> _logger;

    public SqlRoutineExecutor(
        SqliteConnectionFactory connections,
        SqlScriptCatalog scripts,
        ILogger<SqlRoutineExecutor> logger)
    {
        _connections = connections;
        _scripts = scripts;
        _logger = logger;
    }

    public async Task<int> ExecuteAsync(string script, object? parameters, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);

        var affected = await connection.ExecuteAsync(new CommandDefinition(
                _scripts.Read(script),
                parameters,
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        _logger.LogDebug("Rutina {Script} ejecutada ({Affected} fila(s) afectada(s)).", script, affected);
        return affected;
    }

    public async Task<T?> ExecuteScalarAsync<T>(
        string script,
        object? parameters,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);

        return await connection.ExecuteScalarAsync<T>(new CommandDefinition(
                _scripts.Read(script),
                parameters,
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task<TResult> ExecuteInImmediateTransactionAsync<TResult>(
        Func<IRoutineSession, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using var connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);

        // BEGIN IMMEDIATE: toma el bloqueo de escritura de inmediato. Es lo que permite que las rutinas
        // de escritura validen y escriban sin ventanas de carrera entre solicitudes concurrentes.
        await using var transaction = connection.BeginTransaction(deferred: false);
        var session = new RoutineSession(connection, transaction, _scripts);

        var result = await work(session, cancellationToken).ConfigureAwait(false);

        if (session.IsRolledBack)
        {
            _logger.LogDebug("Transaccion de rutinas descartada por el adaptador (sin cambios aplicados).");
            return result;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>Sesion concreta: ejecuta los scripts con la conexion y la transaccion ya abiertas.</summary>
    private sealed class RoutineSession : IRoutineSession
    {
        private readonly SqliteConnection _connection;
        private readonly SqliteTransaction _transaction;
        private readonly SqlScriptCatalog _scripts;

        public RoutineSession(
            SqliteConnection connection,
            SqliteTransaction transaction,
            SqlScriptCatalog scripts)
        {
            _connection = connection;
            _transaction = transaction;
            _scripts = scripts;
        }

        public bool IsRolledBack { get; private set; }

        public Task<int> ExecuteAsync(string script, object? parameters, CancellationToken cancellationToken)
            => _connection.ExecuteAsync(Command(script, parameters, cancellationToken));

        public Task<T?> ExecuteScalarAsync<T>(
            string script,
            object? parameters,
            CancellationToken cancellationToken)
            => _connection.ExecuteScalarAsync<T>(Command(script, parameters, cancellationToken));

        public async Task<IReadOnlyList<T>> QueryAsync<T>(
            string script,
            object? parameters,
            CancellationToken cancellationToken)
        {
            var rows = await _connection
                .QueryAsync<T>(Command(script, parameters, cancellationToken))
                .ConfigureAwait(false);

            return rows.AsList();
        }

        public async Task RollbackAsync(CancellationToken cancellationToken)
        {
            await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            IsRolledBack = true;
        }

        private CommandDefinition Command(string script, object? parameters, CancellationToken cancellationToken)
            => new(_scripts.Read(script), parameters, _transaction, cancellationToken: cancellationToken);
    }
}
