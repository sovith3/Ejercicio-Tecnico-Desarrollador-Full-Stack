using System.ComponentModel.DataAnnotations;
using InvoiceHub.Domain.Enums;

namespace InvoiceHub.Application.Contracts;

/// <summary>Criterios de busqueda de facturas. Se expone como query string en la API.</summary>
public sealed class InvoiceSearchCriteria
{
    /// <summary>Numero exacto de factura.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "El numero de factura debe ser mayor que cero.")]
    public int? Number { get; set; }

    /// <summary>Estado de factura calculado por el sistema (Issued, Partial, Cancelled).</summary>
    public InvoiceStatus? InvoiceStatus { get; set; }

    /// <summary>Estado de cobro calculado por el sistema (Pending, Overdue, Paid).</summary>
    public PaymentStatus? PaymentStatus { get; set; }

    /// <summary>Filtra por consistencia de montos; <c>null</c> devuelve ambos universos.</summary>
    public bool? IsConsistent { get; set; }

    /// <summary>
    /// Cuando es <c>true</c> (por omision) solo devuelve facturas del sistema activo, es decir,
    /// excluye las inconsistentes que se conservan unicamente para reporte.
    /// </summary>
    public bool OnlyActiveSystem { get; set; } = true;

    /// <summary>RUN del cliente (coincidencia parcial).</summary>
    [MaxLength(24, ErrorMessage = "El RUN no puede superar los 24 caracteres.")]
    public string? CustomerRun { get; set; }

    /// <summary>Texto libre que busca en nombre de cliente, correo o producto.</summary>
    [MaxLength(120, ErrorMessage = "El texto de busqueda no puede superar los 120 caracteres.")]
    public string? Text { get; set; }

    /// <summary>Solo facturas con vencimiento superado o por vencer dentro de N dias.</summary>
    [Range(0, 3650, ErrorMessage = "Los dias de mora deben estar entre 0 y 3650.")]
    public int? MinDaysOverdue { get; set; }

    /// <summary>Campo de ordenamiento: number, dueDate, total, customer, outstanding, daysOverdue.</summary>
    public string? SortBy { get; set; } = "number";

    /// <summary>Direccion de ordenamiento: asc o desc.</summary>
    public string? SortDirection { get; set; } = "desc";

    [Range(1, 500, ErrorMessage = "El tamano de pagina debe estar entre 1 y 500.")]
    public int Page { get; set; } = 1;

    [Range(1, 200, ErrorMessage = "El tamano de pagina debe estar entre 1 y 200.")]
    public int PageSize { get; set; } = 25;
}

/// <summary>Factura en el listado de busqueda.</summary>
public sealed record InvoiceListItemDto(
    long Id,
    int Number,
    DateOnly InvoiceDate,
    DateOnly PaymentDueDate,
    int DaysToDue,
    int DaysOverdue,
    string CustomerRun,
    string CustomerName,
    string CustomerEmail,
    decimal TotalAmount,
    decimal CreditNotesTotal,
    decimal OutstandingBalance,
    int CreditNoteCount,
    InvoiceStatus InvoiceStatus,
    PaymentStatus PaymentStatus,
    bool IsConsistent,
    string? InconsistencyReason,
    bool HasPayment,
    string? PaymentMethod,
    DateOnly? PaymentDate);

/// <summary>Linea de detalle de una factura.</summary>
public sealed record InvoiceDetailLineDto(
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal Subtotal,
    decimal ExpectedSubtotal,
    bool HasInconsistentSubtotal);

/// <summary>Nota de credito emitida.</summary>
public sealed record CreditNoteDto(
    long Id,
    int Number,
    DateOnly CreditNoteDate,
    decimal Amount,
    DateTime CreatedAtUtc);

/// <summary>Pago registrado.</summary>
public sealed record InvoicePaymentDto(string PaymentMethod, DateOnly PaymentDate, DateTime RecordedAtUtc);

/// <summary>Detalle completo de una factura, incluyendo sus notas de credito y su estado calculado.</summary>
public sealed record InvoiceDetailDto(
    long Id,
    int Number,
    DateOnly InvoiceDate,
    DateOnly PaymentDueDate,
    int DaysToDue,
    int DaysOverdue,
    string CustomerRun,
    string CustomerName,
    string CustomerEmail,
    decimal TotalAmount,
    decimal DetailsSubtotalSum,
    decimal CreditNotesTotal,
    decimal OutstandingBalance,
    InvoiceStatus InvoiceStatus,
    PaymentStatus PaymentStatus,
    bool IsConsistent,
    string? InconsistencyReason,
    bool HasInconsistentDetailLines,
    InvoiceStatus? DeclaredStatus,
    PaymentStatus? DeclaredPaymentStatus,
    bool IsDeclaredStatusDivergent,
    bool IsDeclaredPaymentStatusDivergent,
    InvoicePaymentDto? Payment,
    IReadOnlyList<InvoiceDetailLineDto> Details,
    IReadOnlyList<CreditNoteDto> CreditNotes,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>Opcion de estado para combos y filtros de la interfaz.</summary>
public sealed record StatusOptionDto<TValue>(TValue Value, string Label, int InvoiceCount, decimal TotalAmount)
    where TValue : struct, Enum;
