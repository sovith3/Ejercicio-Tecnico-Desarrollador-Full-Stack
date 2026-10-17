using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Common;
using InvoiceHub.Domain.Entities;
using InvoiceHub.Domain.Enums;
using InvoiceHub.Domain.Exceptions;

namespace InvoiceHub.Application.UseCases;

/// <summary>Resultado de reconstruir el agregado de una factura desde el origen de datos.</summary>
public sealed record InvoiceBuildResult(Invoice Invoice, IReadOnlyList<string> Notices);

/// <summary>
/// Traduce un registro del archivo JSON al agregado de dominio. Toda la validacion estructural
/// (campos obligatorios, montos, detalle) ocurre aqui; las desviaciones de datos que el negocio
/// permite conservar (montos que no cuadran, notas de credito invalidas) se reportan como avisos y,
/// cuando corresponde, marcan la factura como inconsistente.
/// </summary>
public static class SourceInvoiceFactory
{
    public static InvoiceBuildResult Build(
        SourceInvoice source,
        Customer customer,
        DateOnly today,
        DateTime nowUtc,
        Func<int> nextCreditNoteNumber)
    {
        ArgumentNullException.ThrowIfNull(source);

        var number = source.InvoiceNumber
            ?? throw new InvariantViolationException("El registro no informa el numero de factura.");

        if (source.InvoiceDate is null)
        {
            throw new InvariantViolationException($"La factura {number} no informa fecha de emision.");
        }

        if (source.PaymentDueDate is null)
        {
            throw new InvariantViolationException($"La factura {number} no informa fecha de vencimiento.");
        }

        if (source.TotalAmount is null)
        {
            throw new InvariantViolationException($"La factura {number} no informa monto total.");
        }

        if (source.Details.Count == 0)
        {
            throw new InvariantViolationException($"La factura {number} no contiene lineas de detalle de productos.");
        }

        var notices = new List<string>();
        var invoiceDate = source.InvoiceDate.Value;
        var paymentDueDate = source.PaymentDueDate.Value;
        var computedDaysToDue = Math.Max(0, paymentDueDate.DayNumber - invoiceDate.DayNumber);
        var daysToDue = source.DaysToDue ?? computedDaysToDue;

        if (source.DaysToDue is { } declaredDays && declaredDays != computedDaysToDue)
        {
            notices.Add(
                $"Factura {number}: 'days_to_due' declarado ({declaredDays}) no coincide con las fechas informadas ({computedDaysToDue}).");
        }

        var details = source.Details
            .Select(detail => new InvoiceDetail(
                detail.ProductName ?? string.Empty,
                detail.UnitPrice ?? 0m,
                detail.Quantity ?? 0,
                detail.Subtotal ?? 0m))
            .ToList();

        var payment = InvoicePayment.Create(source.Payment?.PaymentMethod, source.Payment?.PaymentDate, nowUtc);

        var declaredStatus = ParseInvoiceStatus(source.DeclaredStatus);
        if (source.DeclaredStatus is not null && declaredStatus is null)
        {
            notices.Add($"Factura {number}: estado de factura desconocido en el origen ('{source.DeclaredStatus}').");
        }

        var declaredPaymentStatus = ParsePaymentStatus(source.DeclaredPaymentStatus);
        if (source.DeclaredPaymentStatus is not null && declaredPaymentStatus is null)
        {
            notices.Add($"Factura {number}: estado de cobro desconocido en el origen ('{source.DeclaredPaymentStatus}').");
        }

        var invoice = new Invoice(
            number,
            invoiceDate,
            source.TotalAmount.Value,
            daysToDue,
            paymentDueDate,
            customer,
            details,
            payment,
            today,
            nowUtc,
            declaredStatus,
            declaredPaymentStatus);

        AppendCreditNotes(invoice, source, number, invoiceDate, today, nowUtc, nextCreditNoteNumber, notices);

        if (invoice.IsDeclaredStatusDivergent)
        {
            notices.Add(
                $"Factura {number}: el estado declarado ('{source.DeclaredStatus}') difiere del estado calculado ('{invoice.Status}').");
        }

        if (invoice.IsDeclaredPaymentStatusDivergent)
        {
            notices.Add(
                $"Factura {number}: el estado de cobro declarado ('{source.DeclaredPaymentStatus}') difiere del calculado ('{invoice.PaymentStatus}').");
        }

        return new InvoiceBuildResult(invoice, notices);
    }

    private static void AppendCreditNotes(
        Invoice invoice,
        SourceInvoice source,
        int number,
        DateOnly invoiceDate,
        DateOnly today,
        DateTime nowUtc,
        Func<int> nextCreditNoteNumber,
        List<string> notices)
    {
        var reserved = new HashSet<int>();

        foreach (var creditNote in source.CreditNotes)
        {
            var amount = creditNote.CreditNoteAmount ?? 0m;
            if (amount <= 0m)
            {
                notices.Add($"Factura {number}: nota de credito con monto invalido ({amount:N2}); se omite.");
                continue;
            }

            var creditNoteNumber = creditNote.CreditNoteNumber.GetValueOrDefault();
            if (creditNoteNumber <= 0 || !reserved.Add(creditNoteNumber))
            {
                var assigned = nextCreditNoteNumber();
                notices.Add(
                    $"Factura {number}: nota de credito sin numero valido o duplicado; se renumera como {assigned}.");
                creditNoteNumber = assigned;
                reserved.Add(assigned);
            }

            if (amount > invoice.OutstandingBalance)
            {
                invoice.FlagInconsistent(
                    $"La nota de credito {creditNoteNumber} ({MoneyFormat.Amount(amount)}) supera el saldo pendiente de la factura {number}.",
                    nowUtc);
                notices.Add(
                    $"Factura {number}: nota de credito {creditNoteNumber} ({MoneyFormat.Amount(amount)}) supera el saldo pendiente; factura marcada como inconsistente.");
                return;
            }

            try
            {
                invoice.AddCreditNote(
                    creditNoteNumber,
                    creditNote.CreditNoteDate ?? invoiceDate,
                    amount,
                    today,
                    nowUtc);
            }
            catch (DomainException exception)
            {
                notices.Add($"Factura {number}: nota de credito {creditNoteNumber} descartada ({exception.Message}).");
            }
        }
    }

    internal static InvoiceStatus? ParseInvoiceStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "issued" or "emitida" => InvoiceStatus.Issued,
        "partial" or "parcial" => InvoiceStatus.Partial,
        "canceled" or "cancelled" or "cancelada" or "anulada" => InvoiceStatus.Cancelled,
        _ => null
    };

    internal static PaymentStatus? ParsePaymentStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "pending" or "pendiente" => PaymentStatus.Pending,
        "overdue" or "vencida" => PaymentStatus.Overdue,
        "paid" or "pagada" => PaymentStatus.Paid,
        _ => null
    };
}
