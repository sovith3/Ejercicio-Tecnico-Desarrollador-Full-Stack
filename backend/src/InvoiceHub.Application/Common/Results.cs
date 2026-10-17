namespace InvoiceHub.Application.Common;

/// <summary>Excepcion de aplicacion: el recurso solicitado no existe (HTTP 404).</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }

    public static NotFoundException ForInvoice(int number)
        => new($"No se encontro la factura {number}.");
}

/// <summary>Excepcion de aplicacion: la operacion entra en conflicto con el estado actual (HTTP 409).</summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }
}

/// <summary>
/// Excepcion de aplicacion para validaciones de entrada que el model binding no puede expresar
/// (HTTP 400) con detalle por campo.
/// </summary>
public sealed class ValidationFailedException : Exception
{
    public ValidationFailedException(IReadOnlyDictionary<string, string[]> errors)
        : base("La solicitud contiene datos invalidos.")
    {
        Errors = errors;
    }

    public ValidationFailedException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = new[] { message } })
    {
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>Excepcion de aplicacion: no fue posible autenticar la solicitud (HTTP 401).</summary>
public sealed class AuthenticationFailedException : Exception
{
    public AuthenticationFailedException(string message) : base(message)
    {
    }
}

/// <summary>Pagina de resultados con metadatos de paginacion.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasNextPage => Page < TotalPages;

    public bool HasPreviousPage => Page > 1;

    public static PagedResult<T> Empty(int page, int pageSize) => new(Array.Empty<T>(), page, pageSize, 0);
}
