using InvoiceHub.Domain.Exceptions;

namespace InvoiceHub.Domain.Entities;

/// <summary>Pago registrado para una factura.</summary>
public sealed class InvoicePayment
{
    private InvoicePayment()
    {
        PaymentMethod = string.Empty;
    }

    private InvoicePayment(string paymentMethod, DateOnly paymentDate, DateTime recordedAtUtc)
    {
        PaymentMethod = paymentMethod;
        PaymentDate = paymentDate;
        RecordedAtUtc = recordedAtUtc;
    }

    public long Id { get; private set; }

    public long InvoiceId { get; private set; }

    public string PaymentMethod { get; private set; }

    public DateOnly PaymentDate { get; private set; }

    public DateTime RecordedAtUtc { get; private set; }

    public Invoice Invoice { get; private set; } = null!;

    /// <summary>
    /// Construye el pago declarado en el origen de datos. Devuelve <c>null</c> cuando no existe pago
    /// (metodo y fecha nulos), lo que mantiene la factura en estado <c>Pending</c>/<c>Overdue</c>.
    /// </summary>
    public static InvoicePayment? Create(string? paymentMethod, DateOnly? paymentDate, DateTime recordedAtUtc)
    {
        var method = paymentMethod?.Trim();
        if (string.IsNullOrWhiteSpace(method) || paymentDate is null)
        {
            return null;
        }

        return new InvoicePayment(method, paymentDate.Value, recordedAtUtc);
    }

    public static InvoicePayment Create(string paymentMethod, DateOnly paymentDate, DateTime recordedAtUtc)
    {
        var method = paymentMethod?.Trim();
        if (string.IsNullOrWhiteSpace(method))
        {
            throw new BusinessRuleException(DomainErrorCode.InvalidPayment, "El medio de pago es obligatorio.");
        }

        return new InvoicePayment(method, paymentDate, recordedAtUtc);
    }
}
