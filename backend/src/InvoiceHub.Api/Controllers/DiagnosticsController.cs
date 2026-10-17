using InvoiceHub.Api.Models;
using InvoiceHub.Application.Ports;
using InvoiceHub.Infrastructure.Caching;
using InvoiceHub.Infrastructure.Configuration;
using InvoiceHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace InvoiceHub.Api.Controllers;

/// <summary>
/// Diagnostico de la instalacion: archivo de base de datos, huella del esquema aplicado, cache
/// efectiva, origen de datos y ultima importacion. Permite verificar la configuracion sin abrir la
/// base de datos y sin herramientas externas.
/// </summary>
[ApiController]
[Route("api/diagnostics")]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
public sealed class DiagnosticsController : ControllerBase
{
    private readonly DatabaseInitializer _initializer;
    private readonly IInvoiceRepository _invoices;
    private readonly IImportRunRepository _importRuns;
    private readonly IInvoiceSourceReader _sourceReader;
    private readonly ICacheStore _cache;
    private readonly IClock _clock;
    private readonly CacheOptions _cacheOptions;
    private readonly ClockOptions _clockOptions;
    private readonly IHostEnvironment _environment;

    public DiagnosticsController(
        DatabaseInitializer initializer,
        IInvoiceRepository invoices,
        IImportRunRepository importRuns,
        IInvoiceSourceReader sourceReader,
        ICacheStore cache,
        IClock clock,
        IOptions<CacheOptions> cacheOptions,
        IOptions<ClockOptions> clockOptions,
        IHostEnvironment environment)
    {
        _initializer = initializer;
        _invoices = invoices;
        _importRuns = importRuns;
        _sourceReader = sourceReader;
        _cache = cache;
        _clock = clock;
        _cacheOptions = cacheOptions.Value;
        _clockOptions = clockOptions.Value;
        _environment = environment;
    }

    /// <summary>Estado de la instalacion y de sus dependencias.</summary>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    [HttpGet]
    [ProducesResponseType(typeof(DiagnosticsResponse), StatusCodes.Status200OK)]
    public async Task<DiagnosticsResponse> GetAsync(CancellationToken cancellationToken)
    {
        var descriptor = _sourceReader.Describe();
        var lastRun = await _importRuns.GetLatestAsync(cancellationToken);
        var invoiceCount = await _invoices.CountAsync(false, cancellationToken);

        var databaseFile = new FileInfo(_initializer.DatabaseFilePath);

        var database = new DiagnosticsDatabase(
            _initializer.DatabaseFilePath,
            databaseFile.Exists ? databaseFile.Length : 0L,
            _initializer.SchemaFingerprint,
            invoiceCount);

        var cache = new DiagnosticsCache(
            _cache is RedisCacheStore ? "redis" : "memory",
            _cacheOptions.IsRedisEnabled,
            _cacheOptions.InstanceName,
            _cacheOptions.InvoiceTimeToLiveSeconds,
            _cacheOptions.ReportTimeToLiveSeconds,
            _cache is RedisCacheStore redis ? redis.IsConnected : true,
            _cacheOptions.HasPassword,
            _cacheOptions.DescribeEndpoint());

        var sourceFile = new DiagnosticsSourceFile(
            descriptor.FilePath,
            Path.GetFileName(descriptor.FilePath),
            descriptor.Exists,
            descriptor.SizeInBytes,
            descriptor.ModifiedAtUtc);

        var lastImport = lastRun is null
            ? null
            : new DiagnosticsImport(
                lastRun.Id,
                lastRun.SourceFileName,
                lastRun.StartedAtUtc,
                lastRun.FinishedAtUtc,
                lastRun.Status.ToString(),
                lastRun.TotalRecords,
                lastRun.ImportedRecords,
                lastRun.InconsistentRecords,
                lastRun.SkippedRecords,
                lastRun.FailedRecords,
                lastRun.DurationMilliseconds,
                lastRun.Issues);

        var user = new DiagnosticsUser(
            User.Identity?.Name ?? User.FindFirst("name")?.Value,
            User.FindFirst("role")?.Value ?? User.FindFirst("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")?.Value,
            User.Identity?.IsAuthenticated ?? false);

        return new DiagnosticsResponse(
            "InvoiceHub API",
            "1.0.0",
            _environment.EnvironmentName,
            Environment.MachineName,
            _clock.UtcNow,
            _clock.Today,
            _clockOptions.TimeZoneId,
            database,
            cache,
            sourceFile,
            lastImport,
            user);
    }
}
