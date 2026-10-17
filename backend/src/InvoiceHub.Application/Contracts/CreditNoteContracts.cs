using System.ComponentModel.DataAnnotations;
using InvoiceHub.Domain.Enums;

namespace InvoiceHub.Application.Contracts;

/// <summary>Solicitud de emision de una nota de credito.</summary>
public sealed class CreateCreditNoteRequest
{
    /// <summary>
    /// Numero de la nota de credito. Si no se informa, el sistema asigna el siguiente numero
    /// correlativo disponible.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "El numero de nota de credito debe ser mayor que cero.")]
    public int? Number { get; set; }

    /// <summary>
    /// Monto de la nota de credito; no puede superar el saldo pendiente de la factura. Admite pesos con
    /// hasta cinco decimales, igual que el resto del contrato monetario.
    /// </summary>
    [Required(ErrorMessage = "El monto de la nota de credito es obligatorio.")]
    [Range(0.00001, 9_999_999_999.99999, ErrorMessage = "El monto debe ser mayor que cero.")]
    public decimal? Amount { get; set; }

    /// <summary>Fecha de la nota de credito. Si no se informa se usa la fecha de negocio vigente.</summary>
    public DateOnly? CreditNoteDate { get; set; }
}

/// <summary>Resultado de la emision de una nota de credito.</summary>
/// <remarks>
/// Los estados se devuelven como valores del dominio (los mismos que en el detalle y el listado de
/// facturas); la interfaz los traduce a texto para el usuario.
/// </remarks>
public sealed record CreditNoteResultDto(
    int InvoiceNumber,
    int CreditNoteNumber,
    decimal CreditNoteAmount,
    decimal CreditNotesTotal,
    decimal OutstandingBalance,
    InvoiceStatus InvoiceStatus,
    PaymentStatus PaymentStatus,
    DateTime CreatedAtUtc);

/// <summary>Solicitud de registro de pago de una factura.</summary>
public sealed class RegisterPaymentRequest
{
    [Required(ErrorMessage = "El medio de pago es obligatorio.")]
    [MaxLength(60, ErrorMessage = "El medio de pago no puede superar los 60 caracteres.")]
    public string? PaymentMethod { get; set; }

    public DateOnly? PaymentDate { get; set; }
}
