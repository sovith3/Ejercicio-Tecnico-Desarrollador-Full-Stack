using InvoiceHub.Domain.Enums;

namespace InvoiceHub.Application.ReadModels;

/// <summary>
/// Fila de la vista <c>vw_invoice_search</c>. Los montos viajan como enteros de 1/100000 de peso para
/// preservar exactitud en los agregados calculados por el motor; la capa de aplicacion los convierte a
/// pesos con <see cref="Money.FromUnits"/>.
/// </summary>
public sealed class InvoiceSearchRow
{
    public long InvoiceId { get; init; }

    public int Number { get; init; }

    public DateOnly InvoiceDate { get; init; }

    public DateOnly PaymentDueDate { get; init; }

    public int DaysToDue { get; init; }

    public long TotalAmountUnits { get; init; }

    public long CreditNotesTotalUnits { get; init; }

    public long OutstandingBalanceUnits { get; init; }

    public string CustomerRun { get; init; } = string.Empty;

    public string CustomerName { get; init; } = string.Empty;

    public string CustomerEmail { get; init; } = string.Empty;

    public InvoiceStatus InvoiceStatus { get; init; }

    public PaymentStatus PaymentStatus { get; init; }

    public bool IsConsistent { get; init; }

    public string? InconsistencyReason { get; init; }

    public bool HasPayment { get; init; }

    public string? PaymentMethod { get; init; }

    public DateOnly? PaymentDate { get; init; }

    /// <summary>Dias transcurridos desde el vencimiento (0 si no esta vencida).</summary>
    public int DaysOverdue { get; init; }

    /// <summary>Cantidad de notas de credito asociadas a la factura.</summary>
    public int CreditNoteCount { get; init; }
}

/// <summary>Conteo de facturas por estado de factura.</summary>
public sealed class InvoiceStatusCountRow
{
    public InvoiceStatus InvoiceStatus { get; init; }

    public int InvoiceCount { get; init; }

    public long TotalAmountUnits { get; init; }
}

/// <summary>Conteo de facturas por estado de cobro.</summary>
public sealed class PaymentStatusCountRow
{
    public PaymentStatus PaymentStatus { get; init; }

    public int InvoiceCount { get; init; }

    public long TotalAmountUnits { get; init; }
}
