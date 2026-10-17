using InvoiceHub.Application.Common;
using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Entities;

namespace InvoiceHub.Application.Mapping;

/// <summary>
/// Traduce el agregado de factura al contrato de persistencia masiva que consume el puerto
/// <see cref="IInvoiceImportPort"/>. Es el punto en el que los montos pasan a unidades enteras de
/// 1/100000 de peso (aritmetica exacta en el motor) y los enums a sus ordinales; el adaptador solo
/// serializa y el motor solo inserta.
/// </summary>
public static class InvoicePersistMapper
{
    public static InvoicePersistRecord ToRecord(Invoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var details = invoice.Details
            .Select(detail => new InvoicePersistDetail(
                detail.ProductName,
                Money.ToUnits(detail.UnitPrice),
                detail.Quantity,
                Money.ToUnits(detail.Subtotal)))
            .ToList();

        var creditNotes = invoice.CreditNotes
            .Select(note => new InvoicePersistCreditNote(
                note.Number,
                note.CreditNoteDate,
                Money.ToUnits(note.Amount),
                note.CreatedAtUtc))
            .ToList();

        var payment = invoice.Payment is null
            ? null
            : new InvoicePersistPayment(
                invoice.Payment.PaymentMethod,
                invoice.Payment.PaymentDate,
                invoice.Payment.RecordedAtUtc);

        return new InvoicePersistRecord(
            invoice.Number,
            invoice.InvoiceDate,
            Money.ToUnits(invoice.TotalAmount),
            invoice.DaysToDue,
            invoice.PaymentDueDate,
            invoice.Status,
            invoice.PaymentStatus,
            invoice.IsConsistent,
            invoice.InconsistencyReason,
            invoice.DeclaredStatus,
            invoice.DeclaredPaymentStatus,
            invoice.Customer.Run,
            invoice.Customer.Name,
            invoice.Customer.Email,
            details,
            creditNotes,
            payment,
            invoice.CreatedAtUtc,
            invoice.UpdatedAtUtc);
    }
}
