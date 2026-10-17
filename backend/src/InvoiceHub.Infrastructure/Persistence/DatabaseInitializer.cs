using System.Globalization;
using Dapper;
using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Entities;
using InvoiceHub.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceHub.Infrastructure.Persistence;

/// <summary>
/// Inicializador del esquema. En el arranque aplica los scripts SQL versionados de <c>database/scripts</c>
/// (que son la fuente de verdad de la estructura), registra la huella del esquema aplicado y crea el
/// usuario administrador inicial.
/// </summary>
/// <remarks>
/// La huella se calcula solo sobre los scripts DDL: si cambia, la base existente se considera
/// incompatible. En desarrollo y demostracion la base se recrea y el origen se reimporta
/// (<c>Database:RecreateOnSchemaChange</c>); en un entorno donde los datos importan, el inicializador
/// falla de forma explicita en lugar de destruir informacion.
/// </remarks>
public sealed class DatabaseInitializer
{
    /// <summary>Tabla de control creada por el script 01 que registra la huella del esquema aplicado.</summary>
    private const string MigrationsTable = "schema_migrations";

    private const int DdlCommandTimeoutSeconds = 120;

    private readonly SqlScriptCatalog _scripts;
    private readonly SqliteConnectionFactory _connections;
    private readonly DatabaseOptions _options;
    private readonly SecurityOptions _security;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IClock _clock;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        SqlScriptCatalog scripts,
        SqliteConnectionFactory connections,
        IOptions<DatabaseOptions> options,
        IOptions<SecurityOptions> security,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IClock clock,
        ILogger<DatabaseInitializer> logger)
    {
        _scripts = scripts;
        _connections = connections;
        _options = options.Value;
        _security = security.Value;
        _users = users;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Archivo SQLite en uso; se expone para el diagnostico y las pruebas.</summary>
    public string DatabaseFilePath => _connections.FilePath;

    /// <summary>Huella del esquema que corresponden los scripts actuales.</summary>
    public string SchemaFingerprint => _scripts.ComputeSchemaFingerprint();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var missing = _scripts.MissingScripts();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Faltan scripts SQL requeridos en '{_scripts.Directory}': {string.Join(", ", missing)}.");
        }

        _connections.EnsureDirectory();
        await _connections.EnableWriteAheadLoggingAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await SeedAdminUserAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Huella registrada en la base (o <c>null</c> si la base aun no tiene esquema).</summary>
    public async Task<string?> GetAppliedFingerprintAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadFingerprintAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        var fingerprint = _scripts.ComputeSchemaFingerprint();

        await using var connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        var applied = await ReadFingerprintAsync(connection, cancellationToken).ConfigureAwait(false);

        if (string.Equals(applied, fingerprint, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Esquema vigente en {File} (huella {Fingerprint}).",
                _connections.FilePath,
                fingerprint);
            return;
        }

        if (applied is null)
        {
            _logger.LogInformation(
                "Base de datos nueva en {File}: se aplica el esquema desde {Count} scripts.",
                _connections.FilePath,
                SqlScriptCatalog.DdlScripts.Length);
            await ApplyDdlAsync(connection, fingerprint, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!_options.RecreateOnSchemaChange)
        {
            throw new InvalidOperationException(
                $"La base '{_connections.FilePath}' fue creada con otra version del esquema (huella {applied}, " +
                $"esperada {fingerprint}). Elimine el archivo o habilite 'Database:RecreateOnSchemaChange'.");
        }

        _logger.LogWarning(
            "El esquema cambio (huella {Applied} -> {Fingerprint}); se recrea la base {File}.",
            applied,
            fingerprint,
            _connections.FilePath);

        await connection.DisposeAsync().ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        _connections.DeleteDatabaseFiles();

        await using var recreated = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ApplyDdlAsync(recreated, fingerprint, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyDdlAsync(
        SqliteConnection connection,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        foreach (var script in SqlScriptCatalog.DdlScripts)
        {
            await connection
                .ExecuteAsync(new CommandDefinition(
                    _scripts.Read(script),
                    commandTimeout: DdlCommandTimeoutSeconds,
                    cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            _logger.LogDebug("Script de esquema aplicado: {Script}.", script);
        }

        await connection
            .ExecuteAsync(new CommandDefinition(
                $"INSERT INTO {MigrationsTable} (Version, AppliedAtUtc) VALUES (@Version, @AppliedAtUtc);",
                new
                {
                    Version = fingerprint,
                    AppliedAtUtc = _clock.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                },
                commandTimeout: DdlCommandTimeoutSeconds,
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Esquema aplicado (huella {Fingerprint}, {Count} scripts).",
            fingerprint,
            SqlScriptCatalog.DdlScripts.Length);
    }

    private static async Task<string?> ReadFingerprintAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var exists = await connection
            .ExecuteScalarAsync<long?>(new CommandDefinition(
                $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{MigrationsTable}';",
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (exists is null or 0)
        {
            return null;
        }

        return await connection
            .ExecuteScalarAsync<string?>(new CommandDefinition(
                $"SELECT Version FROM {MigrationsTable} ORDER BY AppliedAtUtc DESC, rowid DESC LIMIT 1;",
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    private async Task SeedAdminUserAsync(CancellationToken cancellationToken)
    {
        if (!_options.SeedAdminUser)
        {
            _logger.LogDebug("La creacion del usuario administrador esta deshabilitada por configuracion.");
            return;
        }

        if (await _users.CountAsync(cancellationToken).ConfigureAwait(false) > 0)
        {
            return;
        }

        var admin = new AppUser(
            _security.AdminUsername,
            _security.AdminDisplayName,
            _security.AdminRole,
            _passwordHasher.Hash(_security.AdminPassword),
            _clock.UtcNow);

        await _users.AddAsync(admin, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogWarning(
            "Usuario administrador '{Username}' creado automaticamente para la demostracion; " +
            "cambie la contrasena antes de usar la API en un entorno real.",
            admin.Username);
    }
}
