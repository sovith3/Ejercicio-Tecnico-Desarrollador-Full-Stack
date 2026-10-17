using InvoiceHub.Domain.Common;

namespace InvoiceHub.Domain.Entities;

/// <summary>Nota de credito emitida contra una factura.</summary>
public sealed class CreditNote
{
    private CreditNote()
    {
    }

    /// <summary>
    /// Crea una nota de credito. La fecha de creacion se asigna automaticamente con el reloj de la
    /// aplicacion (requisito: "agregar NC a una factura con fecha de creacion automatica").
    /// </summary>
    internal CreditNote(int number, DateOnly creditNoteDate, decimal amount, DateTime createdAtUtc)
    {
        Number = Guard.Positive(number, nameof(number));
        CreditNoteDate = creditNoteDate;
        Amount = Guard.NotNegative(amount, nameof(amount));
        CreatedAtUtc = createdAtUtc;
    }

    public long Id { get; private set; }

    public long InvoiceId { get; private set; }

    public int Number { get; private set; }

    public DateOnly CreditNoteDate { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>Fecha/hora en que la nota fue registrada en el sistema (automatica).</summary>
    public DateTime CreatedAtUtc { get; private set; }

    public Invoice Invoice { get; private set; } = null!;
}
