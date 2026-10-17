using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using InvoiceHub.Infrastructure.Configuration;

namespace InvoiceHub.Infrastructure.Persistence;

/// <summary>
/// Rutas base del proceso. Se inyectan en lugar de leer <c>Directory.GetCurrentDirectory()</c> en cada
/// adaptador para que las pruebas puedan apuntar a un directorio temporal.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string basePath)
        => BasePath = string.IsNullOrWhiteSpace(basePath) ? Directory.GetCurrentDirectory() : basePath;

    public string BasePath { get; }
}

/// <summary>
/// Fabrica de conexiones SQLite para las consultas y rutinas que no pasan por EF Core (lecturas con
/// Dapper y rutinas almacenadas). Usa exactamente la misma cadena de conexion que el contexto de EF,
/// de modo que ambos trabajan sobre el mismo archivo y el mismo modo de bloqueo.
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly DatabaseOptions _options;
    private readonly ILogger<SqliteConnectionFactory> _logger;
    private readonly string _connectionString;
    private readonly string _filePath;

    static SqliteConnectionFactory() => DapperRules.EnsureRegistered();

    public SqliteConnectionFactory(
        IOptions<DatabaseOptions> options,
        AppPaths paths,
        ILogger<SqliteConnectionFactory> logger)
    {
        _options = options.Value;
        _logger = logger;
        _connectionString = _options.ResolveConnectionString(paths.BasePath);
        _filePath = _options.ResolveFilePath(paths.BasePath);
    }

    /// <summary>Archivo SQLite en uso (se expone para el endpoint de diagnostico y el inicializador).</summary>
    public string FilePath => _filePath;

    public string ConnectionString => _connectionString;

    /// <summary>Crea el directorio del archivo si no existe (arranque en frio dentro de un contenedor).</summary>
    public void EnsureDirectory()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>Abre una conexion y aplica los PRAGMA de sesion (claves foraneas y tiempo de espera).</summary>
    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        EnsureDirectory();

        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await ApplyPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return connection;
    }

    /// <summary>
    /// Activa el modo WAL (se graba en el archivo, por lo que es idempotente) para que las lecturas
    /// concurrentes no se bloqueen durante una importacion o una escritura.
    /// </summary>
    public async Task EnableWriteAheadLoggingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Habilitando el modo WAL en {File}.", _filePath);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Elimina el archivo de la base y sus archivos auxiliares (recreacion del esquema).</summary>
    public void DeleteDatabaseFiles()
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
        {
            var candidate = _filePath + suffix;
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
                _logger.LogWarning("Archivo de base de datos eliminado: {File}", candidate);
            }
        }
    }

    private static async Task ApplyPragmasAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 10000; PRAGMA synchronous = NORMAL;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
