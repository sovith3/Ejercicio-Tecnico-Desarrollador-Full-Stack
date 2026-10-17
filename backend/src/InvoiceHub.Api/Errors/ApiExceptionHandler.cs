using System.Text.Json;
using InvoiceHub.Application.Common;
using InvoiceHub.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Api.Errors;

/// <summary>
/// Traduce las excepciones de dominio, aplicacion y persistencia a respuestas <c>ProblemDetails</c>
/// (RFC 9457). Es el unico lugar donde se decide el codigo HTTP de cada fallo, de modo que los
/// controladores y los casos de uso no conocen el protocolo HTTP.
/// </summary>
public sealed class ApiExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<ApiExceptionHandler> _logger;

    public ApiExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<ApiExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Si el cliente aborto la solicitud ya no hay a quien responder.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Solicitud cancelada por el cliente: {Method} {Path}.",
                httpContext.Request.Method,
                httpContext.Request.Path);

            return true;
        }

        var (status, title, extension) = Map(exception);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                exception,
                "Error no controlado en {Method} {Path}.",
                httpContext.Request.Method,
                httpContext.Request.Path);
        }
        else
        {
            _logger.LogWarning(
                "Solicitud rechazada ({Status}) en {Method} {Path}: {Message}",
                status,
                httpContext.Request.Method,
                httpContext.Request.Path,
                exception.Message);
        }

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = status >= StatusCodes.Status500InternalServerError
                ? "Se produjo un error inesperado al procesar la solicitud."
                : exception.Message,
            Instance = httpContext.Request.Path
        };

        extension?.Invoke(problemDetails);

        httpContext.Response.StatusCode = status;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception
        });
    }

    /// <summary>Codigo HTTP, titulo y datos adicionales que corresponden a cada excepcion conocida.</summary>
    private static (int Status, string Title, Action<ProblemDetails>? Extension) Map(Exception exception)
    {
        switch (exception)
        {
            case ValidationFailedException validation:
                return (
                    StatusCodes.Status400BadRequest,
                    "Solicitud invalida",
                    problem => problem.Extensions["errors"] = validation.Errors.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value));

            case BusinessRuleException business:
                return (
                    StatusCodes.Status422UnprocessableEntity,
                    "Regla de negocio no satisfecha",
                    problem => problem.Extensions["businessCode"] = business.Code.ToString());

            case InvariantViolationException:
            case DomainException:
                return (StatusCodes.Status422UnprocessableEntity, "Datos invalidos", null);

            case NotFoundException:
                return (StatusCodes.Status404NotFound, "Recurso no encontrado", null);

            case ConflictException:
                return (StatusCodes.Status409Conflict, "Operacion en conflicto", null);

            case AuthenticationFailedException:
                return (StatusCodes.Status401Unauthorized, "No autenticado", null);

            case FileNotFoundException:
            case DirectoryNotFoundException:
                return (
                    StatusCodes.Status409Conflict,
                    "Origen de datos no disponible",
                    null);

            case InvalidDataException:
            case JsonException:
                return (
                    StatusCodes.Status422UnprocessableEntity,
                    "Origen de datos invalido",
                    null);

            case DbUpdateException update:
                return (
                    StatusCodes.Status409Conflict,
                    "Conflicto con los datos existentes",
                    problem => problem.Extensions["constraint"] = update.InnerException?.Message);

            case SqliteException sqlite when sqlite.SqliteErrorCode == 19:
                return (
                    StatusCodes.Status409Conflict,
                    "Conflicto con los datos existentes",
                    problem => problem.Extensions["sqliteExtendedCode"] = sqlite.SqliteExtendedErrorCode);

            case SqliteException sqlite when sqlite.SqliteErrorCode is 5 or 6:
                return (StatusCodes.Status503ServiceUnavailable, "Base de datos ocupada", null);

            case OperationCanceledException:
                return (
                    StatusCodes.Status503ServiceUnavailable,
                    "Tiempo de espera agotado",
                    null);

            default:
                return (StatusCodes.Status500InternalServerError, "Error interno del servidor", null);
        }
    }
}
