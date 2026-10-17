using System.Text.Json;
using System.Text.Json.Serialization;
using InvoiceHub.Api.Configuration;
using InvoiceHub.Api.Errors;
using InvoiceHub.Api.HealthChecks;
using InvoiceHub.Api.Models;
using InvoiceHub.Api.Serialization;
using InvoiceHub.Application.Ports;
using InvoiceHub.Application.UseCases;
using InvoiceHub.Infrastructure;
using InvoiceHub.Infrastructure.Configuration;
using InvoiceHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

const string frontendCorsPolicy = "invoicehub-frontend";

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------------------------
// Configuracion tipada
// ---------------------------------------------------------------------------------------------
builder.Services.AddOptions<ApiHostOptions>()
    .Bind(builder.Configuration.GetSection(ApiHostOptions.SectionName))
    .ValidateOnStart();

var securityOptions = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>()
    ?? new SecurityOptions();

// ---------------------------------------------------------------------------------------------
// Servicios de la aplicacion: capas de dominio, aplicacion e infraestructura
// ---------------------------------------------------------------------------------------------
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // Los enums viajan como texto ("Overdue" en lugar de 2) para que el contrato publicado en
        // Swagger y el consumo desde la interfaz sean legibles. Los nulos se omiten para no enviar
        // campos vacios que el cliente interpreta como cambios. Los montos se escriben como decimales
        // sin exponente (0.00001), coherentes con los cinco decimales del contrato monetario.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.Converters.Add(new PlainDecimalJsonConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

// ---------------------------------------------------------------------------------------------
// Errores: ProblemDetails uniforme para toda la API
// ---------------------------------------------------------------------------------------------
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        context.ProblemDetails.Extensions["timestampUtc"] = DateTime.UtcNow;
    };
});

builder.Services.AddExceptionHandler<ApiExceptionHandler>();

// ---------------------------------------------------------------------------------------------
// Autenticacion JWT y autorizacion
// ---------------------------------------------------------------------------------------------
var signingKey = securityOptions.ResolveSigningKey();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = securityOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = securityOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(signingKey),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Cuando la configuracion exige autenticacion (Security:RequireAuthentication) toda la API queda
    // protegida; con el valor por omision solo las operaciones de escritura piden token.
    if (securityOptions.RequireAuthentication)
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    }
});

// ---------------------------------------------------------------------------------------------
// CORS para la interfaz web
// ---------------------------------------------------------------------------------------------
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
if (allowedOrigins is null || allowedOrigins.Length == 0)
{
    allowedOrigins = new[] { "http://localhost:5173", "http://localhost:4173", "http://localhost:3000" };
}

var allowAnyOrigin = Array.IndexOf(allowedOrigins, "*") >= 0;

builder.Services.AddCors(options => options.AddPolicy(frontendCorsPolicy, policy =>
{
    if (allowAnyOrigin)
    {
        policy.AllowAnyOrigin();
    }
    else
    {
        policy.WithOrigins(allowedOrigins);
    }

    policy.AllowAnyHeader().AllowAnyMethod();
}));

// ---------------------------------------------------------------------------------------------
// Swagger / OpenAPI con esquema JWT
// ---------------------------------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "InvoiceHub API",
        Version = "v1",
        Description = "API de facturacion: busqueda de facturas, notas de credito, pagos, importacion del "
            + "origen JSON, reportes y panel de control. Las operaciones de escritura requieren un token "
            + "JWT obtenido en POST /api/auth/login."
    });

    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Token JWT precedido por la palabra Bearer. Ejemplo: Bearer eyJhbGciOi...",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };

    options.AddSecurityDefinition("Bearer", securityScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [securityScheme] = Array.Empty<string>()
    });

    foreach (var documentationFile in Directory.GetFiles(AppContext.BaseDirectory, "*.xml"))
    {
        options.IncludeXmlComments(documentationFile, includeControllerXmlComments: true);
    }
});

// ---------------------------------------------------------------------------------------------
// Sondas de salud (liveness / readiness)
// ---------------------------------------------------------------------------------------------
builder.Services
    .AddHealthChecks()
    .AddCheck<SqliteDatabaseHealthCheck>("sqlite", tags: new[] { "ready" })
    .AddCheck<CacheHealthCheck>("cache", tags: new[] { "ready" })
    .AddCheck<SourceFileHealthCheck>("source-file", tags: new[] { "ready" });

var app = builder.Build();

// ---------------------------------------------------------------------------------------------
// Canalizacion de la solicitud
// ---------------------------------------------------------------------------------------------
app.UseExceptionHandler();
app.UseStatusCodePages();

var apiHostOptions = app.Services.GetRequiredService<IOptions<ApiHostOptions>>().Value;

if (apiHostOptions.SwaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "InvoiceHub API v1");
        options.DocumentTitle = "InvoiceHub API";
        options.RoutePrefix = "swagger";
    });
}

if (apiHostOptions.UseHttpsRedirection)
{
    app.UseHttpsRedirection();
}

if (apiHostOptions.ServeStaticFiles)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.UseCors(frontendCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Informe de salud en JSON estable: /health (todas las sondas), /health/live y /health/ready.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = WriteHealthReportAsync
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteHealthReportAsync
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthReportAsync
});

app.MapGet("/", (IOptions<ApiHostOptions> options) => Results.Ok(BuildApiInfo(options.Value, app.Environment.EnvironmentName)));

// La interfaz compilada se sirve solo si existe: en desarrollo corre en Vite y las rutas de la API
// conservan sus respuestas 404 con ProblemDetails.
var webRootPath = app.Environment.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
if (apiHostOptions.ServeStaticFiles && File.Exists(Path.Combine(webRootPath, "index.html")))
{
    app.MapFallbackToFile("index.html");
}

// ---------------------------------------------------------------------------------------------
// Arranque: esquema de la base de datos, usuario administrador y carga inicial del origen JSON
// ---------------------------------------------------------------------------------------------
using (var startupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
{
    await using var scope = app.Services.CreateAsyncScope();

    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync(startupTimeout.Token);

    app.Logger.LogInformation(
        "Base de datos lista en {File} (esquema {Fingerprint}).",
        initializer.DatabaseFilePath,
        initializer.SchemaFingerprint);

    var sourceData = scope.ServiceProvider.GetRequiredService<IOptions<SourceDataOptions>>().Value;
    if (sourceData.ImportOnStartup)
    {
        await ImportOnStartupAsync(scope.ServiceProvider, app.Logger, startupTimeout.Token);
    }
}

await app.RunAsync();

// Carga inicial: importa el archivo JSON unicamente cuando la base de datos esta vacia. Es idempotente
// (el importador hace upsert), pero se omite cuando ya hay datos para que un reinicio no repita la
// lectura completa del origen.
static async Task ImportOnStartupAsync(
    IServiceProvider services,
    ILogger logger,
    CancellationToken cancellationToken)
{
    var repository = services.GetRequiredService<IInvoiceRepository>();
    var existing = await repository.CountAsync(false, cancellationToken);

    if (existing > 0)
    {
        logger.LogInformation(
            "Carga inicial omitida: la base de datos ya contiene {Count} facturas.",
            existing);
        return;
    }

    var importHandler = services.GetRequiredService<ImportInvoicesHandler>();
    var summary = await importHandler.HandleAsync(cancellationToken);

    logger.LogInformation(
        "Carga inicial desde {File}: {Imported} importadas, {Inconsistent} inconsistentes, "
        + "{Skipped} omitidas y {Failed} fallidas en {Duration} ms.",
        summary.SourceFile,
        summary.Imported,
        summary.Inconsistent,
        summary.Skipped,
        summary.Failed,
        summary.DurationMilliseconds);
}

// Documento de descubrimiento: que es la API y en que rutas esta cada recurso.
static ApiInfoResponse BuildApiInfo(ApiHostOptions options, string environmentName) => new(
    "InvoiceHub API",
    "1.0.0",
    environmentName,
    "API de facturacion sobre SQLite: busqueda y detalle de facturas, notas de credito, registro de "
        + "pagos, importacion idempotente del origen JSON, reportes agregados y panel de control.",
    new Dictionary<string, string>
    {
        ["openapi"] = options.SwaggerEnabled ? "/swagger/v1/swagger.json" : "deshabilitado",
        ["swagger"] = options.SwaggerEnabled ? "/swagger" : "deshabilitado",
        ["health"] = "/health",
        ["healthLiveness"] = "/health/live",
        ["healthReadiness"] = "/health/ready",
        ["login"] = "POST /api/auth/login",
        ["currentUser"] = "GET /api/auth/me",
        ["searchInvoices"] = "GET /api/invoices",
        ["invoiceDetail"] = "GET /api/invoices/{number}",
        ["invoiceStatusCatalog"] = "GET /api/invoices/status-catalog",
        ["addCreditNote"] = "POST /api/invoices/{number}/credit-notes",
        ["registerPayment"] = "POST /api/invoices/{number}/payments",
        ["sourceStatus"] = "GET /api/imports/source",
        ["importInvoices"] = "POST /api/imports",
        ["dashboard"] = "GET /api/dashboard",
        ["paymentStatusSummary"] = "GET /api/reports/payment-status-summary",
        ["overdueWithoutActivity"] = "GET /api/reports/overdue-without-activity",
        ["inconsistentInvoices"] = "GET /api/reports/inconsistent-invoices",
        ["diagnostics"] = "GET /api/diagnostics"
    });

// Escribe el informe de salud como JSON estable, apto para sondas y para consumo humano.
static Task WriteHealthReportAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json; charset=utf-8";

    var payload = new HealthReportResponse(
        report.Status.ToString(),
        DateTime.UtcNow,
        Math.Round(report.TotalDuration.TotalMilliseconds, 2),
        report.Entries
            .Select(entry => new HealthEntryResponse(
                entry.Key,
                entry.Value.Status.ToString(),
                entry.Value.Description,
                Math.Round(entry.Value.Duration.TotalMilliseconds, 2),
                entry.Value.Data.ToDictionary(pair => pair.Key, pair => pair.Value?.ToString())))
            .ToList());

    return context.Response.WriteAsJsonAsync(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}

/// <summary>Punto de entrada expuesto para las pruebas de integracion de la API.</summary>
public partial class Program
{
}
