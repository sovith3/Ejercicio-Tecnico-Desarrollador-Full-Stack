using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace InvoiceHub.Infrastructure.Persistence;

/// <summary>
/// Aplica los PRAGMA de sesion sobre cada conexion que EF abre. <c>foreign_keys</c> se repite aqui
/// (ademas de la cadena de conexion) porque SQLite lo evalua por conexion y una conexion creada por el
/// proveedor podria reutilizarse desde el pool sin la configuracion esperada; <c>busy_timeout</c> evita
/// fallos inmediatos cuando una importacion y una lectura concurrente compiten por el archivo.
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string PragmaScript =
        "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 10000; PRAGMA synchronous = NORMAL;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Apply(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private static void Apply(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = PragmaScript;
        command.ExecuteNonQuery();
    }

    private static async Task ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = PragmaScript;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
