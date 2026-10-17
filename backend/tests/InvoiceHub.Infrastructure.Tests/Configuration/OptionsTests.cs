using InvoiceHub.Infrastructure.Configuration;
using InvoiceHub.Infrastructure.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InvoiceHub.Infrastructure.Tests.Configuration;

/// <summary>
/// Pruebas de las opciones de configuracion y del reloj del sistema. La resolucion de rutas, la cadena de
/// conexion, la vigencia de cache, la clave de firma y la zona horaria son decisiones que condicionan el
/// arranque y el calculo de la mora.
/// </summary>
public sealed class OptionsTests
{
    private static readonly string BasePath = Path.Combine(Path.GetTempPath(), "invoicehub-opciones");

    [Fact]
    public void DatabaseOptions_ResolvesRelativeFilePathAgainstTheBasePath()
    {
        var options = new DatabaseOptions { FilePath = Path.Combine("data", "invoicehub.db") };

        var resolved = options.ResolveFilePath(BasePath);

        Assert.True(Path.IsPathRooted(resolved));
        Assert.Equal(Path.GetFullPath(Path.Combine(BasePath, "data", "invoicehub.db")), resolved);
    }

    [Fact]
    public void DatabaseOptions_KeepsAnAbsoluteFilePath()
    {
        var absolute = Path.Combine(BasePath, "absoluto.db");
        var options = new DatabaseOptions { FilePath = absolute };

        Assert.Equal(Path.GetFullPath(absolute), options.ResolveFilePath(BasePath));
    }

    [Fact]
    public void DatabaseOptions_BuildsAConnectionStringWithForeignKeysAndTimeout()
    {
        var options = new DatabaseOptions { FilePath = Path.Combine("data", "invoicehub.db") };

        var connectionString = options.ResolveConnectionString(BasePath);

        Assert.Contains("invoicehub.db", connectionString);
        Assert.Contains("Foreign Keys=True", connectionString);
        Assert.Contains("Default Timeout=30", connectionString);
    }

    [Fact]
    public void DatabaseOptions_PrefersTheExplicitConnectionString()
    {
        var options = new DatabaseOptions
        {
            FilePath = "ignorado.db",
            ConnectionString = "Data Source=:memory:;Cache=Shared"
        };

        Assert.Equal("Data Source=:memory:;Cache=Shared", options.ResolveConnectionString(BasePath));
    }

    [Theory]
    [InlineData("Redis", true, true)]
    [InlineData("redis", true, true)]
    [InlineData("Memory", true, false)]
    [InlineData("Redis", false, false)]
    public void CacheOptions_EnablesRedisOnlyWhenProviderAndFlagAgree(string provider, bool useRedis, bool expected)
    {
        var options = new CacheOptions { Provider = provider, UseRedis = useRedis };

        Assert.Equal(expected, options.IsRedisEnabled);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(30, 30)]
    [InlineData(100_000, 3600)]
    public void CacheOptions_ClampsTimeToLiveSeconds(int configured, int expectedSeconds)
    {
        var options = new CacheOptions
        {
            InvoiceTimeToLiveSeconds = configured,
            ReportTimeToLiveSeconds = configured
        };

        Assert.Equal(expectedSeconds, (int)options.InvoiceTimeToLive.TotalSeconds);
        Assert.Equal(expectedSeconds, (int)options.ReportTimeToLive.TotalSeconds);
    }

    [Fact]
    public void ClockOptions_ResolvesTheConfiguredTimeZone()
    {
        var options = new ClockOptions { TimeZoneId = "America/Santiago" };

        var timeZone = options.ResolveTimeZone();

        Assert.Equal("America/Santiago", timeZone.Id);
    }

    [Fact]
    public void ClockOptions_WithUnknownTimeZone_FallsBackToTheLocalZone()
    {
        var options = new ClockOptions { TimeZoneId = "Zona/Inexistente" };

        Assert.Equal(TimeZoneInfo.Local.Id, options.ResolveTimeZone().Id);
    }

    [Fact]
    public void ClockOptions_WithoutTimeZone_FallsBackToTheLocalZone()
    {
        var options = new ClockOptions { TimeZoneId = "  " };

        Assert.Equal(TimeZoneInfo.Local.Id, options.ResolveTimeZone().Id);
    }

    [Fact]
    public void SystemClock_ReportsUtcTimestampsAndTheBusinessDateInTheConfiguredZone()
    {
        var clock = new SystemClock(Options.Create(new ClockOptions { TimeZoneId = "America/Santiago" }));

        Assert.Equal(DateTimeKind.Utc, clock.UtcNow.Kind);

        var expectedToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Santiago")));

        Assert.Equal(expectedToday, clock.Today);
    }

    [Fact]
    public void SecurityOptions_PadsShortKeysToTheMinimumLength()
    {
        var options = new SecurityOptions { Key = "corta" };

        var key = options.ResolveSigningKey();

        Assert.Equal(32, key.Length);
    }

    [Fact]
    public void SecurityOptions_UsesTheFullKeyWhenItIsLongEnough()
    {
        var options = new SecurityOptions { Key = "clave-de-firma-de-pruebas-2026-abcdefgh" };

        Assert.Equal(options.Key, System.Text.Encoding.UTF8.GetString(options.ResolveSigningKey()));
    }

    [Fact]
    public void SecurityOptions_ClampsTokenLifetimeMinutes()
    {
        var options = new SecurityOptions { TokenLifetimeMinutes = 10_000 };

        Assert.Equal(TimeSpan.FromMinutes(1440), options.TokenLifetime);
    }

    [Fact]
    public void SourceDataOptions_ResolvesTheFilePathOnlyWhenItIsNotRooted()
    {
        var relative = new SourceDataOptions { FilePath = Path.Combine("data", "origen.json") };
        var absolute = new SourceDataOptions { FilePath = Path.Combine(BasePath, "origen.json") };

        Assert.Equal(Path.GetFullPath(Path.Combine(BasePath, "data", "origen.json")), relative.ResolveFilePath(BasePath));
        Assert.Equal(Path.GetFullPath(absolute.FilePath), absolute.ResolveFilePath(BasePath));
    }

    [Fact]
    public void CacheOptions_ComposesTheEndpointFromHostAndPortWhenThereIsNoConnectionString()
    {
        var options = new CacheOptions { ConnectionString = null, Host = "redis-cache", Port = 6380 };

        var configuration = options.ResolveConfiguration();

        Assert.Contains("redis-cache:6380", configuration.ToString(includePassword: false));
        Assert.False(configuration.AbortOnConnectFail);
    }

    [Fact]
    public void CacheOptions_AppliesTheStructuredCredentialsOverTheParsedConnectionString()
    {
        var options = new CacheOptions
        {
            ConnectionString = "redis-cache:6379,abortConnect=false",
            User = "invoicehub",
            Password = "clave-de-redis",
            Database = 3,
            UseSsl = true
        };

        var configuration = options.ResolveConfiguration();

        Assert.Equal("invoicehub", configuration.User);
        Assert.Equal("clave-de-redis", configuration.Password);
        Assert.Equal(3, configuration.DefaultDatabase);
        Assert.True(configuration.Ssl);
        Assert.True(options.HasPassword);
    }

    [Fact]
    public void CacheOptions_NeverExposesThePasswordInTheEndpointDescription()
    {
        var options = new CacheOptions { ConnectionString = "redis-cache:6379", Password = "clave-de-redis" };

        var description = options.DescribeEndpoint();

        Assert.Contains("redis-cache:6379", description);
        Assert.DoesNotContain("clave-de-redis", description);
    }

    [Fact]
    public void CacheOptions_WithAnInvalidConnectionString_DescribesTheFailureWithoutThrowing()
    {
        var options = new CacheOptions { ConnectionString = "redis-cache:99999" };

        Assert.Equal("(configuracion de Redis invalida)", options.DescribeEndpoint());
    }

    [Fact]
    public void CacheOptions_OverrideConnectionString_PrefersTheInjectedValueAndIgnoresEmptyOnes()
    {
        var options = new CacheOptions { ConnectionString = "localhost:6379" };

        options.OverrideConnectionString("  redis-cache:6379,password=desde-entorno  ");
        Assert.Equal("redis-cache:6379,password=desde-entorno", options.ConnectionString);

        options.OverrideConnectionString("   ");
        Assert.Equal("redis-cache:6379,password=desde-entorno", options.ConnectionString);

        options.OverrideConnectionString(null);
        Assert.Equal("redis-cache:6379,password=desde-entorno", options.ConnectionString);
    }

    [Fact]
    public void AddInfrastructure_ReceivesTheRedisConnectionStringFromTheStandardHierarchy()
    {
        using var host = new TestHost(overrides: new Dictionary<string, string?>
        {
            ["Cache:ConnectionString"] = "localhost:6379",
            ["ConnectionStrings:RedisConnection"] = "redis-cache:6379,password=desde-compose,abortConnect=false"
        });

        var options = host.Provider.GetRequiredService<IOptions<CacheOptions>>().Value;

        Assert.Equal("redis-cache:6379,password=desde-compose,abortConnect=false", options.ConnectionString);
        Assert.Equal(
            "redis-cache:6379",
            options.DescribeEndpoint().Split(',')[0]);
    }
}
