using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.UseCases;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceHub.Api.Controllers;

/// <summary>Panel principal: indicadores consolidados, series y principales deudores.</summary>
[ApiController]
[Route("api/dashboard")]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
public sealed class DashboardController : ControllerBase
{
    private readonly GetDashboardHandler _dashboard;

    public DashboardController(GetDashboardHandler dashboard) => _dashboard = dashboard;

    /// <summary>Indicadores consolidados del panel, calculados dentro del motor de base de datos.</summary>
    /// <param name="minDaysOverdue">Dias de mora con los que se clasifica una factura como vencida.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    [HttpGet]
    [ProducesResponseType(typeof(DashboardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public Task<DashboardDto> GetAsync(
        [FromQuery] int minDaysOverdue = 30,
        CancellationToken cancellationToken = default)
        => _dashboard.HandleAsync(minDaysOverdue, cancellationToken);
}
