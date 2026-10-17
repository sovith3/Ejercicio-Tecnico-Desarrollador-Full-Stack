using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.ReadModels;
using InvoiceHub.Domain.Entities;
using InvoiceHub.Domain.Enums;

namespace InvoiceHub.Application.Mapping;

/// <summary>
/// Traduce modelos de lectura y agregados de dominio a los contratos publicos de la API.
/// Los montos salen del motor de base de datos como enteros de 1/100000 de peso y se convierten aqui
/// con <see cref="Money.FromUnits"/> para que la API entregue pesos con hasta cinco decimales.
/// </summary>
public static class InvoiceDtoMapper
{
    public static InvoiceListItemDto ToListItem(InvoiceSearchRow row) => new(
        row.InvoiceId,
        row.Number,
        row.InvoiceDate,
        row.PaymentDueDate,
        row.DaysToDue,
        row.DaysOverdue,
        row.CustomerRun,
        row.CustomerName,
        row.CustomerEmail,
        Money.FromUnits(row.TotalAmountUnits),
        Money.FromUnits(row.CreditNotesTotalUnits),
        Money.FromUnits(row.OutstandingBalanceUnits),
        row.CreditNoteCount,
        row.InvoiceStatus,
        row.PaymentStatus,
        row.IsConsistent,
        row.InconsistencyReason,
        row.HasPayment,
        row.PaymentMethod,
        row.PaymentDate);

    public static InvoiceDetailDto ToDetail(Invoice invoice, DateOnly today)
    {
        var daysOverdue = invoice.PaymentStatus == PaymentStatus.Overdue
            ? today.DayNumber - invoice.PaymentDueDate.DayNumber
            : 0;

        return new InvoiceDetailDto(
            invoice.Id,
            invoice.Number,
            invoice.InvoiceDate,
            invoice.PaymentDueDate,
            invoice.DaysToDue,
            daysOverdue,
            invoice.Customer.Run,
            invoice.Customer.Name,
            invoice.Customer.Email,
            invoice.TotalAmount,
            invoice.DetailsSubtotalSum,
            invoice.CreditNotesTotal,
            invoice.OutstandingBalance,
            invoice.Status,
            invoice.PaymentStatus,
            invoice.IsConsistent,
            invoice.InconsistencyReason,
            invoice.HasInconsistentDetailLines,
            invoice.DeclaredStatus,
            invoice.DeclaredPaymentStatus,
            invoice.IsDeclaredStatusDivergent,
            invoice.IsDeclaredPaymentStatusDivergent,
            ToPaymentDto(invoice.Payment),
            invoice.Details.OrderBy(detail => detail.Id).Select(ToDetailLine).ToList(),
            invoice.CreditNotes
                .OrderBy(note => note.CreditNoteDate)
                .ThenBy(note => note.Number)
                .Select(ToCreditNoteDto)
                .ToList(),
            invoice.CreatedAtUtc,
            invoice.UpdatedAtUtc);
    }

    public static InvoiceDetailLineDto ToDetailLine(InvoiceDetail detail) => new(
        detail.ProductName,
        detail.UnitPrice,
        detail.Quantity,
        detail.Subtotal,
        detail.ExpectedSubtotal,
        detail.HasInconsistentSubtotal);

    public static CreditNoteDto ToCreditNoteDto(CreditNote creditNote) => new(
        creditNote.Id,
        creditNote.Number,
        creditNote.CreditNoteDate,
        creditNote.Amount,
        creditNote.CreatedAtUtc);

    public static InvoicePaymentDto? ToPaymentDto(InvoicePayment? payment)
        => payment is null ? null : new InvoicePaymentDto(payment.PaymentMethod, payment.PaymentDate, payment.RecordedAtUtc);

    public static string InvoiceStatusLabel(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Issued => "Emitida",
        InvoiceStatus.Partial => "Parcial",
        InvoiceStatus.Cancelled => "Anulada",
        _ => status.ToString()
    };

    public static string PaymentStatusLabel(PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => "Pendiente",
        PaymentStatus.Overdue => "Vencida",
        PaymentStatus.Paid => "Pagada",
        _ => status.ToString()
    };
}
