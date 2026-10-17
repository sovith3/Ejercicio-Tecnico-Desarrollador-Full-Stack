using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceHub.Api.Controllers;

/// <summary>
/// Importacion del archivo JSON de origen. Cada ejecucion queda registrada en <c>import_runs</c> con
/// sus contadores e incidencias, de modo que el estado del origen es auditable.
/// </summary>
[ApiController]
[Route("api/imports")]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
public sealed class ImportsController : ControllerBase
{
    private readonly ImportInvoicesHandler _import;
    private readonly GetSourceFileStatusHandler _sourceStatus;

    public ImportsController(ImportInvoicesHandler import, GetSourceFileStatusHandler sourceStatus)
    {
        _import = import;
        _sourceStatus = sourceStatus;
    }

    /// <summary>Describe el archivo JSON configurado y la ultima importacion registrada.</summary>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    [HttpGet("source")]
    [ProducesResponseType(typeof(SourceFileStatusDto), StatusCodes.Status200OK)]
    public Task<SourceFileStatusDto> GetSourceStatusAsync(CancellationToken cancellationToken)
        => _sourceStatus.HandleAsync(cancellationToken);

    /// <summary>
    /// Importa (o reimporta) las facturas del archivo JSON. La operacion es idempotente: las facturas
    /// ya almacenadas se actualizan y las lineas se reemplazan por las del archivo.
    /// </summary>
    /// <remarks>
    /// Las facturas cuyo total declarado no coincide con la suma de los subtotales de sus productos se
    /// conservan marcadas como inconsistentes: quedan fuera del sistema activo (no admiten notas de
    /// credito ni pagos) pero se informan en el reporte de inconsistencias.
    /// </remarks>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(ImportSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public Task<ImportSummaryDto> ImportAsync(CancellationToken cancellationToken)
        => _import.HandleAsync(cancellationToken);
}
