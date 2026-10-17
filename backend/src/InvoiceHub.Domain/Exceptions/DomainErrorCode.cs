namespace InvoiceHub.Domain.Exceptions;

/// <summary>
/// Codigos estables de reglas de negocio. El codigo es parte del contrato con los consumidores de la
/// API (se publica en <c>ProblemDetails.extensions.businessCode</c>) y con el frontend.
/// </summary>
public enum DomainErrorCode
{
    /// <summary>La factura no es consistente y por lo tanto esta fuera del sistema activo.</summary>
    InconsistentInvoice = 1000,

    /// <summary>El monto de la nota de credito no es mayor que cero.</summary>
    CreditNoteAmountNotPositive = 1001,

    /// <summary>El monto de la nota de credito supera el saldo pendiente de la factura.</summary>
    CreditNoteExceedsOutstandingBalance = 1002,

    /// <summary>La factura ya fue cancelada por notas de credito.</summary>
    InvoiceAlreadyCancelled = 1003,

    /// <summary>El pago informado no es valido.</summary>
    InvalidPayment = 1004,

    /// <summary>Los datos de la factura no cumplen los invariantes del dominio.</summary>
    InvalidInvoiceData = 1005
}
