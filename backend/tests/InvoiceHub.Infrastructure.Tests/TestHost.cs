using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Entities;
using InvoiceHub.Infrastructure;
using InvoiceHub.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InvoiceHub.Infrastructure.Tests;

/// <summary>
/// Host de pruebas de infraestructura: compone el contenedor real de dependencias apuntando a una base
/// SQLite temporal y a los scripts SQL versionados del repositorio. Cada instancia es independiente, de
/// modo que las pruebas ejercitan el esquema, las rutinas y las consultas reales sin estado compartido.
/// </summary>
internal sealed class TestHost : IDisposable
{
    public const string AdminUsername = "admin";
    public const string AdminPassword = "Clave.De.Pruebas.2026";

    private static readonly string RepositoryRoot = ResolveRepositoryRoot();

    /// <summary>Carpeta real de scripts SQL del repositorio (fuente de verdad del esquema).</summary>
    public static string ScriptsPath { get; } =
        Path.Combine(RepositoryRoot, "backend", "database", "scripts");

    /// <summary>Archivo JSON del enunciado, usado como origen de datos real en las pruebas.</summary>
    public static string SourceFilePath { get; } = Path.Combine(RepositoryRoot, "bd_exam_invoices.json");

    public TestHost(
        bool seedAdminUser = true,
        IDictionary<string, string?>? overrides = null)
    {
        RootPath = Path.Combine(Path.GetTempPath(), "invoicehub-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(RootPath);
        DatabaseFilePath = Path.Combine(RootPath, "invoicehub.db");

        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Database:FilePath"] = DatabaseFilePath,
            ["Database:ScriptsPath"] = ScriptsPath,
            ["Database:RecreateOnSchemaChange"] = "true",
            ["Database:SeedAdminUser"] = seedAdminUser ? "true" : "false",
            ["SourceData:FilePath"] = SourceFilePath,
            ["Cache:Provider"] = "Memory",
            ["Cache:UseRedis"] = "false",
            ["Clock:TimeZoneId"] = "America/Santiago",
            ["Security:Issuer"] = "InvoiceHub.Tests",
            ["Security:Audience"] = "InvoiceHub.Tests.Client",
            ["Security:Key"] = "clave-de-firma-para-pruebas-automatizadas-2026",
            ["Security:AdminUsername"] = AdminUsername,
            ["Security:AdminPassword"] = AdminPassword
        };

        if (overrides is not null)
        {
            foreach (var pair in overrides)
            {
                settings[pair.Key] = pair.Value;
            }
        }

        Configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(Configuration, RootPath);

        Provider = services.BuildServiceProvider();
    }

    public IConfigurationRoot Configuration { get; }

    public ServiceProvider Provider { get; }

    public string RootPath { get; }

    public string DatabaseFilePath { get; }

    /// <summary>Crea la base de datos, aplica los scripts de esquema y siembra el usuario administrador.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = Provider.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<DatabaseInitializer>()
            .InitializeAsync(cancellationToken);
    }

    public async Task WithScopeAsync(Func<IServiceProvider, Task> action)
    {
        await using var scope = Provider.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    /// <summary>
    /// Persiste una factura por el camino de escritura real (EF Core) y devuelve su identificador, de modo
    /// que las rutinas y las consultas se prueben contra datos consistentes.
    /// </summary>
    public async Task<long> AddInvoiceAsync(Invoice invoice, CancellationToken cancellationToken = default)
    {
        await WithScopeAsync(async provider =>
        {
            await provider.GetRequiredService<IInvoiceRepository>().AddAsync(invoice, cancellationToken);
            await provider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
        });

        return invoice.Id;
    }

    /// <summary>Ejecuta una consulta escalar directa contra el archivo SQLite en uso.</summary>
    public async Task<T?> ExecuteScalarAsync<T>(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabaseFilePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T));
    }

    /// <summary>Ejecuta una sentencia directa contra el archivo SQLite en uso (soporte de las pruebas).</summary>
    public async Task<int> ExecuteNonQueryAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabaseFilePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return await command.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        Provider.Dispose();
        SqliteConnection.ClearAllPools();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (System.IO.Directory.Exists(RootPath))
                {
                    System.IO.Directory.Delete(RootPath, recursive: true);
                }

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(50);
            }
        }
    }

    /// <summary>Localiza la raiz del repositorio a partir de la carpeta de binarios de las pruebas.</summary>
    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "bd_exam_invoices.json")) &&
                System.IO.Directory.Exists(Path.Combine(current.FullName, "backend", "database", "scripts")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            $"No se encontro la raiz del repositorio (backend/database/scripts) desde '{AppContext.BaseDirectory}'.");
    }
}
