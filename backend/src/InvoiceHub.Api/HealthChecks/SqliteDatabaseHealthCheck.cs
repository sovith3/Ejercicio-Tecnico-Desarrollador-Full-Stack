using InvoiceHub.Application.Ports;
using InvoiceHub.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace InvoiceHub.Api.HealthChecks;

/// <summary>
/// Verifica la base de datos SQLite: abre una conexion con la misma cadena que usa la aplicacion,
/// ejecuta una consulta real y expone el archivo, el esquema aplicado y la cantidad de facturas.
/// </summary>
public sealed class SqliteDatabaseHealthCheck : IHealthCheck
{
    private readonly SqliteConnectionFactory _connections;
    private readonly DatabaseInitializer _initializer;
    private readonly IInvoiceRepository _invoices;

    public SqliteDatabaseHealthCheck(
        SqliteConnectionFactory connections,
        DatabaseInitializer initializer,
        IInvoiceRepository invoices)
    {
        _connections = connections;
        _initializer = initializer;
        _invoices = invoices;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _connections.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM invoices;";

            var total = Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));

            var file = new FileInfo(_connections.FilePath);
            var persisted = await _invoices.CountAsync(false, cancellationToken).ConfigureAwait(false);

            var data = new Dictionary<string, object>
            {
                ["file"] = _connections.FilePath,
                ["sizeInBytes"] = file.Exists ? file.Length : 0L,
                ["schemaFingerprint"] = _initializer.SchemaFingerprint,
                ["invoices"] = total,
                ["persistedInvoices"] = persisted
            };

            return HealthCheckResult.Healthy(
                $"Base de datos disponible con {total} facturas registradas.",
                data);
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "No fue posible consultar la base de datos.",
                exception,
                new Dictionary<string, object> { ["file"] = _connections.FilePath });
        }
    }
}
