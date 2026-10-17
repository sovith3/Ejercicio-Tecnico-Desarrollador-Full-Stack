using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceHub.Api.Controllers;

/// <summary>
/// Facturas: busqueda paginada sobre la vista <c>vw_invoice_search</c>, detalle completo, catalogo de
/// estados y las operaciones de escritura (notas de credito y registro de pago).
/// </summary>
[ApiController]
[Route("api/invoices")]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
public sealed class InvoicesController : ControllerBase
{
    /// <summary>Nombre de la ruta del detalle: usado para construir la cabecera <c>Location</c>.</summary>
    public const string DetailRouteName = "invoices.detail";

    private readonly SearchInvoicesHandler _search;
    private readonly GetInvoiceDetailHandler _detail;
    private readonly GetInvoiceStatusCatalogHandler _statusCatalog;
    private readonly AddCreditNoteHandler _creditNotes;
    private readonly RegisterInvoicePaymentHandler _payments;

    public InvoicesController(
        SearchInvoicesHandler search,
        GetInvoiceDetailHandler detail,
        GetInvoiceStatusCatalogHandler statusCatalog,
        AddCreditNoteHandler creditNotes,
        RegisterInvoicePaymentHandler payments)
    {
        _search = search;
        _detail = detail;
        _statusCatalog = statusCatalog;
        _creditNotes = creditNotes;
        _payments = payments;
    }

    /// <summary>Busca facturas con filtros combinables, ordenamiento y paginacion.</summary>
    /// <remarks>
    /// Los filtros de estado corresponden a los valores calculados por el sistema, no a los declarados
    /// en el archivo de origen. Con <c>onlyActiveSystem=true</c> (valor por omision) se excluyen las
    /// facturas inconsistentes, que quedan disponibles para el reporte de inconsistencias.
    /// </remarks>
    /// <param name="criteria">Criterios de busqueda recibidos por query string.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<InvoiceListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public Task<PagedResult<InvoiceListItemDto>> SearchAsync(
        [FromQuery] InvoiceSearchCriteria criteria,
        CancellationToken cancellationToken)
        => _search.HandleAsync(criteria, cancellationToken);

    /// <summary>Obtiene el detalle de una factura por su numero.</summary>
    /// <param name="number">Numero de factura.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    [HttpGet("{number:int}", Name = DetailRouteName)]
    [ProducesResponseType(typeof(InvoiceDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<InvoiceDetailDto> GetByNumberAsync(int number, CancellationToken cancellationToken)
        => _detail.HandleAsync(number, cancellationToken);

    /// <summary>Catalogo de estados con la cantidad y el monto acumulado de cada uno.</summary>
    /// <param name="onlyActiveSystem">Excluye las facturas inconsistentes cuando es <c>true</c>.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    [HttpGet("status-catalog")]
    [ProducesResponseType(typeof(InvoiceStatusCatalogDto), StatusCodes.Status200OK)]
    public Task<InvoiceStatusCatalogDto> GetStatusCatalogAsync(
        [FromQuery] bool onlyActiveSystem,
        CancellationToken cancellationToken)
        => _statusCatalog.HandleAsync(onlyActiveSystem, cancellationToken);

    /// <summary>Emite una nota de credito sobre una factura del sistema activo.</summary>
    /// <remarks>
    /// El monto no puede superar el saldo pendiente y se valida de forma atomica dentro de SQLite, por
    /// lo que dos solicitudes concurrentes no pueden dejar el saldo en negativo. Si no se informa el
    /// numero, se asigna el siguiente correlativo disponible.
    /// </remarks>
    /// <param name="number">Numero de la factura a acreditar.</param>
    /// <param name="request">Monto y fecha de la nota de credito.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    [HttpPost("{number:int}/credit-notes")]
    [Authorize]
    [ProducesResponseType(typeof(CreditNoteResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreditNoteResultDto>> AddCreditNoteAsync(
        int number,
        [FromBody] CreateCreditNoteRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _creditNotes.HandleAsync(number, request, cancellationToken);

        // Se nombra la ruta explicitamente: MVC suprime el sufijo "Async" en los nombres de accion, de
        // modo que CreatedAtAction(nameof(GetByNumberAsync), ...) no encontraria la ruta.
        return CreatedAtRoute(DetailRouteName, new { number }, result);
    }

    /// <summary>Registra el pago de una factura. Cada factura admite un unico pago.</summary>
    /// <remarks>
    /// El pago se registra una sola vez por factura y solo sobre facturas del sistema activo y no
    /// anuladas. Un segundo intento o un intento sobre una factura anulada se rechaza con 409.
    /// </remarks>
    /// <param name="number">Numero de la factura a pagar.</param>
    /// <param name="request">Medio de pago y fecha de pago.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    [HttpPost("{number:int}/payments")]
    [Authorize]
    [ProducesResponseType(typeof(InvoiceDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public Task<InvoiceDetailDto> RegisterPaymentAsync(
        int number,
        [FromBody] RegisterPaymentRequest request,
        CancellationToken cancellationToken)
        => _payments.HandleAsync(number, request, cancellationToken);
}
