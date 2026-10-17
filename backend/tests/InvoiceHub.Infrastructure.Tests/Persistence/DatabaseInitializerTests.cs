using InvoiceHub.Application.Ports;
using InvoiceHub.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceHub.Infrastructure.Tests.Persistence;

/// <summary>
/// Pruebas del inicializador de esquema. Verifican que los scripts SQL del repositorio sean la fuente de
/// verdad de la estructura, que la huella del esquema quede registrada, que la inicializacion sea
/// idempotente y que un cambio de huella no destruya datos de forma silenciosa.
/// </summary>
public sealed class DatabaseInitializerTests : IAsyncLifetime
{
    private readonly TestHost _host = new();

    public Task InitializeAsync() => _host.InitializeAsync();

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task InitializeAsync_AppliesTablesIndexesAndSearchView()
    {
        var tables = await _host.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN (" +
            "'customers','invoices','invoice_details','credit_notes','invoice_payments'," +
            "'import_runs','import_run_issues','app_users','schema_migrations');");

        var view = await _host.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'view' AND name = 'vw_invoice_search';");

        var indexes = await _host.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name LIKE 'ix_%';");

        Assert.Equal(9L, tables);
        Assert.Equal(1L, view);
        Assert.True(indexes >= 10, $"Se esperaban al menos 10 indices, se encontraron {indexes}.");
    }

    [Fact]
    public async Task InitializeAsync_RecordsTheFingerprintOfTheAppliedSchema()
    {
        var (expected, applied) = await _host.WithScopeAsync(async provider =>
        {
            var initializer = provider.GetRequiredService<DatabaseInitializer>();
            return (initializer.SchemaFingerprint, await initializer.GetAppliedFingerprintAsync(CancellationToken.None));
        });

        Assert.NotNull(applied);
        Assert.Equal(expected, applied);
        Assert.StartsWith("schema-", applied);
        Assert.Equal(23, applied!.Length);
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotentAndPreservesExistingData()
    {
        var invoiceId = await _host.AddInvoiceAsync(TestData.Invoice(number: 5001));

        await _host.InitializeAsync();

        var migrations = await _host.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM schema_migrations;");
        var invoices = await _host.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM invoices;");
        var users = await _host.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM app_users;");

        Assert.True(invoiceId > 0);
        Assert.Equal(1L, migrations);
        Assert.Equal(1L, invoices);
        Assert.Equal(1L, users);
    }

    [Fact]
    public async Task InitializeAsync_SeedsTheAdminUserWithHashedPassword()
    {
        var user = await _host.WithScopeAsync(provider => provider
            .GetRequiredService<IUserRepository>()
            .FindByUsernameAsync(TestHost.AdminUsername, CancellationToken.None));

        var hasher = _host.Provider.GetRequiredService<IPasswordHasher>();

        Assert.NotNull(user);
        Assert.Equal("Administrator", user!.Role);
        Assert.True(user.IsActive);
        Assert.NotEqual(TestHost.AdminPassword, user.PasswordHash);
        Assert.True(hasher.Verify(TestHost.AdminPassword, user.PasswordHash));
        Assert.False(hasher.Verify("otra-clave", user.PasswordHash));
    }

    [Fact]
    public async Task InitializeAsync_CalledTwice_DoesNotDuplicateTheAdminUser()
    {
        await _host.InitializeAsync();

        var count = await _host.WithScopeAsync(provider => provider
            .GetRequiredService<IUserRepository>()
            .CountAsync(CancellationToken.None));

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task InitializeAsync_WhenSeedingIsDisabled_LeavesTheUserTableEmpty()
    {
        using var host = new TestHost(seedAdminUser: false);

        await host.InitializeAsync();

        var count = await host.WithScopeAsync(provider => provider
            .GetRequiredService<IUserRepository>()
            .CountAsync(CancellationToken.None));

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task InitializeAsync_WhenScriptsAreMissing_ThrowsWithTheMissingFileNames()
    {
        using var host = new TestHost(overrides: new Dictionary<string, string?>
        {
            ["Database:ScriptsPath"] = Path.Combine(Path.GetTempPath(), "invoicehub-scripts-inexistentes")
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.InitializeAsync());

        Assert.Contains("Faltan scripts SQL", exception.Message);
        Assert.Contains(SqlScriptCatalog.DdlScripts[0], exception.Message);
        Assert.Contains(SqlScriptCatalog.Dashboard, exception.Message);
    }

    [Fact]
    public async Task InitializeAsync_WhenTheSchemaFingerprintChanges_RecreatesTheDatabase()
    {
        await _host.AddInvoiceAsync(TestData.Invoice(number: 5002));

        // Se simula una base creada con otra version del esquema.
        await _host.ExecuteNonQueryAsync("UPDATE schema_migrations SET Version = 'schema-0000000000000000';");

        await _host.InitializeAsync();

        var invoices = await _host.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM invoices;");
        var migrations = await _host.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM schema_migrations;");
        var applied = await _host.ExecuteScalarAsync<string>(
            "SELECT Version FROM schema_migrations ORDER BY rowid DESC LIMIT 1;");

        Assert.Equal(0L, invoices);
        Assert.Equal(1L, migrations);
        Assert.StartsWith("schema-", applied);
        Assert.NotEqual("schema-0000000000000000", applied);
    }

    [Fact]
    public async Task InitializeAsync_WhenRecreateIsDisabledAndTheFingerprintChanged_Throws()
    {
        using var host = new TestHost(overrides: new Dictionary<string, string?>
        {
            ["Database:RecreateOnSchemaChange"] = "false"
        });

        await host.InitializeAsync();
        await host.ExecuteNonQueryAsync("UPDATE schema_migrations SET Version = 'schema-0000000000000000';");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.InitializeAsync());

        Assert.Contains("otra version del esquema", exception.Message);
        Assert.Contains("RecreateOnSchemaChange", exception.Message);
    }
}
