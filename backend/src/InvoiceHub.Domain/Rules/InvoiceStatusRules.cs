using InvoiceHub.Domain.Common;
using InvoiceHub.Domain.Enums;

namespace InvoiceHub.Domain.Rules;

/// <summary>
/// Reglas de calculo automatico de estados. Se implementan como funciones puras para poder
/// reutilizarlas desde las pruebas y desde el dominio, y para que la logica SQL equivalente
/// (<c>sp_RecalculateInvoiceStatuses</c>) tenga una unica especificacion de referencia.
/// </summary>
public static class InvoiceStatusRules
{
    /// <summary>
    /// Resuelve el estado de la factura a partir del monto acumulado de notas de credito.
    /// </summary>
    /// <param name="creditNotesTotal">Suma de los montos de las notas de credito de la factura.</param>
    /// <param name="invoiceTotal">Monto total declarado de la factura.</param>
    public static InvoiceStatus ResolveInvoiceStatus(decimal creditNotesTotal, decimal invoiceTotal)
    {
        if (creditNotesTotal <= 0m)
        {
            return InvoiceStatus.Issued;
        }

        // Un monto de credito igual o superior al total se consideral cancelacion total de la factura.
        return creditNotesTotal >= invoiceTotal
            ? InvoiceStatus.Cancelled
            : InvoiceStatus.Partial;
    }

    /// <summary>
    /// Resuelve el estado de cobro: el pago registrado tiene prioridad; en su ausencia la factura esta
    /// vencida si la fecha de vencimiento es anterior a la fecha de evaluacion.
    /// </summary>
    public static PaymentStatus ResolvePaymentStatus(bool hasPayment, DateOnly paymentDueDate, DateOnly today)
    {
        if (hasPayment)
        {
            return PaymentStatus.Paid;
        }

        return today > paymentDueDate
            ? PaymentStatus.Overdue
            : PaymentStatus.Pending;
    }

    /// <summary>
    /// Determina si la factura es consistente: el total declarado debe coincidir exactamente con la
    /// suma de los subtotales de sus productos.
    /// </summary>
    public static bool IsTotalConsistent(decimal declaredTotal, decimal detailsSubtotalSum)
        => declaredTotal == detailsSubtotalSum;

    /// <summary>Motivo legible de la inconsistencia detectada, o <c>null</c> si no hay desviacion.</summary>
    public static string? DescribeInconsistency(decimal declaredTotal, decimal detailsSubtotalSum)
        => IsTotalConsistent(declaredTotal, detailsSubtotalSum)
            ? null
            : $"El total declarado ({MoneyFormat.Amount(declaredTotal)}) no coincide con la suma de subtotales de sus productos ({MoneyFormat.Amount(detailsSubtotalSum)}).";
}
