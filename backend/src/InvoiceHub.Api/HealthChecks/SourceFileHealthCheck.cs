using InvoiceHub.Application.Ports;
using InvoiceHub.Infrastructure.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace InvoiceHub.Api.HealthChecks;

/// <summary>
/// Comprueba que el archivo JSON de origen configurado existe y respeta el tamano maximo admitido.
/// Un archivo ausente no inutiliza la API (la base de datos ya contiene los datos importados), por lo
/// que se informa como <c>Degraded</c>.
/// </summary>
public sealed class SourceFileHealthCheck : IHealthCheck
{
    private readonly IInvoiceSourceReader _sourceReader;
    private readonly IOptions<SourceDataOptions> _options;

    public SourceFileHealthCheck(IInvoiceSourceReader sourceReader, IOptions<SourceDataOptions> options)
    {
        _sourceReader = sourceReader;
        _options = options;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var descriptor = _sourceReader.Describe();

        var data = new Dictionary<string, object>
        {
            ["file"] = descriptor.FilePath,
            ["sizeInBytes"] = descriptor.SizeInBytes,
            ["modifiedAtUtc"] = descriptor.ModifiedAtUtc?.ToString("O") ?? "n/a",
            ["maxFileSizeBytes"] = _options.Value.MaxFileSizeBytes
        };

        if (!descriptor.Exists)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"El archivo de origen {descriptor.FilePath} no existe; se sirven unicamente los datos ya importados.",
                data: data));
        }

        if (descriptor.SizeInBytes > _options.Value.MaxFileSizeBytes)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"El archivo de origen supera el tamano maximo admitido ({_options.Value.MaxFileSizeBytes} bytes).",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Archivo de origen disponible ({descriptor.SizeInBytes} bytes).",
            data));
    }
}
