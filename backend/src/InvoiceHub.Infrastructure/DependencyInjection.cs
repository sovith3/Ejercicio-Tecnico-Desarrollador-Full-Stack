using InvoiceHub.Application;
using InvoiceHub.Application.Ports;
using InvoiceHub.Infrastructure.Caching;
using InvoiceHub.Infrastructure.Configuration;
using InvoiceHub.Infrastructure.Persistence;
using InvoiceHub.Infrastructure.Persistence.Readers;
using InvoiceHub.Infrastructure.Persistence.Repositories;
using InvoiceHub.Infrastructure.Persistence.Routines;
using InvoiceHub.Infrastructure.Security;
using InvoiceHub.Infrastructure.SourceData;
using InvoiceHub.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceHub.Infrastructure;

/// <summary>
/// Composicion de la infraestructura: unica funcion que conoce simultaneamente los puertos de la capa de
/// aplicacion y sus adaptadores (EF Core para escritura, Dapper para lectura, rutinas SQLite, cache
/// Redis/memoria, archivo JSON de origen, seguridad y reloj). La API solo invoca este registro y no
/// necesita saber que tecnologia hay detras de cada puerto.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string basePath)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(new AppPaths(basePath));
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<SourceDataOptions>(configuration.GetSection(SourceDataOptions.SectionName));
        services.Configure<CacheOptions>(configuration.GetSection(CacheOptions.SectionName));

        // El entorno inyecta la cadena de conexion de Redis con la jerarquia estandar de .NET
        // (ConnectionStrings__RedisConnection). Tiene prioridad sobre Cache:ConnectionString para que host,
        // puerto y contrasena se declaren una sola vez (en el .env del compose) y no en dos lugares.
        services.PostConfigure<CacheOptions>(options =>
            options.OverrideConnectionString(configuration.GetConnectionString(CacheOptions.ConnectionStringName)));
        services.Configure<ClockOptions>(configuration.GetSection(ClockOptions.SectionName));
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));

        services.AddApplicationUseCases();

        services.AddDbContext<AppDbContext>((provider, builder) =>
        {
            var database = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            var paths = provider.GetRequiredService<AppPaths>();

            builder.UseSqlite(database.ResolveConnectionString(paths.BasePath));
            builder.AddInterceptors(new SqlitePragmaInterceptor());
            builder.EnableDetailedErrors();
        });

        // Esquema y carga inicial.
        services.AddSingleton<SqlScriptCatalog>();
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddScoped<DatabaseInitializer>();

        // Escritura: repositorios sobre EF Core y unidad de trabajo.
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IImportRunRepository, ImportRunRepository>();
        services.AddScoped<IUserRepository, UserRepository>();

        // Lectura: consultas Dapper sobre la vista y las consultas de reporte.
        services.AddScoped<IInvoiceSearchReader, InvoiceSearchReader>();
        services.AddScoped<IInvoiceReportReader, InvoiceReportReader>();

        // Escritura pesada y calculo dentro del motor de base de datos: un adaptador por caso de uso, de
        // modo que cada handler dependa unicamente de la rutina que necesita (segregacion de interfaces).
        services.AddScoped<IRoutineExecutor, SqlRoutineExecutor>();
        services.AddScoped<IStatusRecalculationPort, StatusRecalculationRoutine>();
        services.AddScoped<ICreditNoteWritePort, CreditNoteWriteRoutine>();
        services.AddScoped<IPaymentWritePort, PaymentWriteRoutine>();
        services.AddScoped<IInvoiceImportPort, InvoiceImportRoutine>();

        // Adaptadores de entrada, cache, reloj y seguridad.
        services.AddSingleton<IInvoiceSourceReader, InvoiceSourceReader>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ISecurityTokenIssuer, JwtSecurityTokenIssuer>();
        services.AddSingleton<ICacheStore>(CreateCacheStore);

        return services;
    }

    /// <summary>
    /// Cache efectiva: Redis cuando esta habilitado y configurado, memoria en cualquier otro caso. El
    /// adaptador de Redis degrada por si mismo a memoria si el servidor no responde, de modo que la
    /// resolucion del servicio nunca falle en el arranque.
    /// </summary>
    private static ICacheStore CreateCacheStore(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<CacheOptions>>().Value;
        var memory = new MemoryCacheStore();

        if (!options.IsRedisEnabled)
        {
            provider
                .GetRequiredService<ILogger<MemoryCacheStore>>()
                .LogInformation(
                    "Cache en memoria habilitada (Cache:Provider = {Provider}).",
                    options.Provider);

            return memory;
        }

        return new RedisCacheStore(
            options,
            memory,
            provider.GetRequiredService<ILogger<RedisCacheStore>>());
    }
}
