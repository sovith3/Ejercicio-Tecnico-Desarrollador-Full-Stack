using InvoiceHub.Domain.Common;

namespace InvoiceHub.Domain.Exceptions;

/// <summary>Excepcion base de las reglas de negocio expresadas por el dominio.</summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message)
    {
    }

    protected DomainException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>
/// Se lanza cuando una operacion viola una regla de negocio del dominio. El adaptador HTTP la
/// traduce a <c>422 Unprocessable Entity</c> incluyendo <see cref="Code"/>.
/// </summary>
public sealed class BusinessRuleException : DomainException
{
    public BusinessRuleException(DomainErrorCode code, string message) : base(message)
    {
        Code = code;
    }

    public DomainErrorCode Code { get; }

    public static BusinessRuleException InconsistentInvoice(int invoiceNumber, string reason)
        => new(
            DomainErrorCode.InconsistentInvoice,
            $"La factura {invoiceNumber} esta fuera del sistema activo por inconsistencia: {reason}");

    public static BusinessRuleException CreditNoteExceedsOutstanding(
        decimal requested,
        decimal outstanding,
        int invoiceNumber)
        => new(
            DomainErrorCode.CreditNoteExceedsOutstandingBalance,
            $"La nota de credito de {MoneyFormat.Amount(requested)} supera el saldo pendiente de la factura {invoiceNumber} ({MoneyFormat.Amount(outstanding)}).");

    public static BusinessRuleException AlreadyCancelled(int invoiceNumber)
        => new(
            DomainErrorCode.InvoiceAlreadyCancelled,
            $"La factura {invoiceNumber} ya se encuentra cancelada: el saldo pendiente es cero.");

    public static BusinessRuleException CreditNoteAmountNotPositive(decimal amount)
        => new(
            DomainErrorCode.CreditNoteAmountNotPositive,
            $"El monto de la nota de credito debe ser mayor que cero (recibido {MoneyFormat.Amount(amount)}).");
}

/// <summary>Se lanza cuando los datos de entrada no respetan los invariantes del dominio.</summary>
public sealed class InvariantViolationException : DomainException
{
    public InvariantViolationException(string message) : base(message)
    {
    }

    public InvariantViolationException(string message, Exception inner) : base(message, inner)
    {
    }
}
