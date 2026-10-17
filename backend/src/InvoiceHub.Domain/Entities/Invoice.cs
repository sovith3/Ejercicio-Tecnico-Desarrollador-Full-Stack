using InvoiceHub.Domain.Common;
using InvoiceHub.Domain.Enums;
using InvoiceHub.Domain.Exceptions;
using InvoiceHub.Domain.Rules;

namespace InvoiceHub.Domain.Entities;

/// <summary>
/// Raiz de agregado de facturacion. Concentra las reglas de consistencia, el calculo automatico de
/// estados y la gestion de notas de credito.
/// </summary>
public sealed class Invoice
{
    private readonly List<InvoiceDetail> _details = new();
    private readonly List<CreditNote> _creditNotes = new();

    private Invoice()
    {
        InconsistencyReason = null;
    }

    public Invoice(
        int number,
        DateOnly invoiceDate,
        decimal totalAmount,
        int daysToDue,
        DateOnly paymentDueDate,
        Customer customer,
        IEnumerable<InvoiceDetail> details,
        InvoicePayment? payment,
        DateOnly today,
        DateTime nowUtc,
        InvoiceStatus? declaredStatus = null,
        PaymentStatus? declaredPaymentStatus = null)
    {
        Number = Guard.Positive(number, nameof(number));
        InvoiceDate = invoiceDate;
        TotalAmount = Guard.NotNegative(totalAmount, nameof(totalAmount));
        DaysToDue = Guard.NotNegative(daysToDue, nameof(daysToDue));
        PaymentDueDate = paymentDueDate;
        Customer = Guard.NotNull(customer, nameof(customer));
        Payment = payment;
        DeclaredStatus = declaredStatus;
        DeclaredPaymentStatus = declaredPaymentStatus;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;

        _details.AddRange(Guard.NotNull(details, nameof(details)));
        if (_details.Count == 0)
        {
            throw new InvariantViolationException($"La factura {number} no contiene lineas de detalle.");
        }

        EvaluateConsistency();
        RefreshStatuses(today);
    }

    public long Id { get; private set; }

    /// <summary>Numero de factura. Identificador natural unico (requisito: "invoice_number" unico).</summary>
    public int Number { get; private set; }

    public DateOnly InvoiceDate { get; private set; }

    public decimal TotalAmount { get; private set; }

    public int DaysToDue { get; private set; }

    public DateOnly PaymentDueDate { get; private set; }

    /// <summary>Estado calculado por el sistema a partir de las notas de credito.</summary>
    public InvoiceStatus Status { get; private set; }

    /// <summary>Estado de cobro calculado por el sistema.</summary>
    public PaymentStatus PaymentStatus { get; private set; }

    /// <summary>
    /// Indica si la factura pertenece al sistema activo. Una factura inconsistente se conserva en la
    /// base para reporte pero queda excluida de la operacion.
    /// </summary>
    public bool IsConsistent { get; private set; }

    public string? InconsistencyReason { get; private set; }

    /// <summary>Estado declarado en el origen de datos; se conserva solo con fines de auditoria.</summary>
    public InvoiceStatus? DeclaredStatus { get; private set; }

    public PaymentStatus? DeclaredPaymentStatus { get; private set; }

    public long CustomerId { get; private set; }

    public Customer Customer { get; private set; } = null!;

    public InvoicePayment? Payment { get; private set; }

    public IReadOnlyCollection<InvoiceDetail> Details => _details;

    public IReadOnlyCollection<CreditNote> CreditNotes => _creditNotes;

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Suma de los subtotales informados por las lineas de detalle.</summary>
    public decimal DetailsSubtotalSum => _details.Sum(detail => detail.Subtotal);

    /// <summary>Suma acumulada de las notas de credito emitidas contra la factura.</summary>
    public decimal CreditNotesTotal => _creditNotes.Sum(note => note.Amount);

    /// <summary>Saldo pendiente de la factura luego de aplicar las notas de credito.</summary>
    public decimal OutstandingBalance
    {
        get
        {
            var balance = TotalAmount - CreditNotesTotal;
            return balance > 0m ? balance : 0m;
        }
    }

    public bool HasCreditNotes => _creditNotes.Count > 0;

    public bool HasPayment => Payment is not null;

    /// <summary>Indica si existe al menos una linea cuyo subtotal no cuadra con precio por cantidad.</summary>
    public bool HasInconsistentDetailLines => _details.Any(detail => detail.HasInconsistentSubtotal);

    public bool IsDeclaredStatusDivergent => DeclaredStatus.HasValue && DeclaredStatus.Value != Status;

    public bool IsDeclaredPaymentStatusDivergent =>
        DeclaredPaymentStatus.HasValue && DeclaredPaymentStatus.Value != PaymentStatus;

    /// <summary>Recalcula la bandera de consistencia comparando el total declarado con el detalle.</summary>
    public void EvaluateConsistency()
    {
        var sum = DetailsSubtotalSum;
        IsConsistent = InvoiceStatusRules.IsTotalConsistent(TotalAmount, sum);
        InconsistencyReason = InvoiceStatusRules.DescribeInconsistency(TotalAmount, sum);
    }

    /// <summary>Recalcula estado de factura y estado de cobro.</summary>
    public void RefreshStatuses(DateOnly today)
    {
        Status = InvoiceStatusRules.ResolveInvoiceStatus(CreditNotesTotal, TotalAmount);
        PaymentStatus = InvoiceStatusRules.ResolvePaymentStatus(HasPayment, PaymentDueDate, today);
    }

    /// <summary>Recalcula consistencia y ambos estados, actualizando la marca de tiempo.</summary>
    public void Refresh(DateOnly today, DateTime nowUtc)
    {
        EvaluateConsistency();
        RefreshStatuses(today);
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Valida que la factura pertenezca al sistema activo (consistente).</summary>
    public void EnsureIsActive()
    {
        if (!IsConsistent)
        {
            throw BusinessRuleException.InconsistentInvoice(Number, InconsistencyReason ?? "desviacion de montos");
        }
    }

    /// <summary>
    /// Registra una nota de credito validando que el monto no supere el saldo pendiente. La fecha de
    /// creacion se asigna automaticamente con el reloj recibido.
    /// </summary>
    public CreditNote AddCreditNote(
        int number,
        DateOnly creditNoteDate,
        decimal amount,
        DateOnly today,
        DateTime nowUtc)
    {
        EnsureIsActive();

        if (amount <= 0m)
        {
            throw BusinessRuleException.CreditNoteAmountNotPositive(amount);
        }

        if (Status == InvoiceStatus.Cancelled)
        {
            throw BusinessRuleException.AlreadyCancelled(Number);
        }

        var outstanding = OutstandingBalance;
        if (amount > outstanding)
        {
            throw BusinessRuleException.CreditNoteExceedsOutstanding(amount, outstanding, Number);
        }

        if (_creditNotes.Any(note => note.Number == number))
        {
            throw new BusinessRuleException(
                DomainErrorCode.InvalidInvoiceData,
                $"La nota de credito {number} ya se encuentra registrada en la factura {Number}.");
        }

        var creditNote = new CreditNote(number, creditNoteDate, amount, nowUtc);
        _creditNotes.Add(creditNote);
        RefreshStatuses(today);
        UpdatedAtUtc = nowUtc;

        return creditNote;
    }

    /// <summary>Registra el pago de la factura.</summary>
    public void RegisterPayment(string paymentMethod, DateOnly paymentDate, DateOnly today, DateTime nowUtc)
    {
        EnsureIsActive();
        Payment = InvoicePayment.Create(paymentMethod, paymentDate, nowUtc);
        RefreshStatuses(today);
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Marca la factura como no consistente indicando el motivo detectado.</summary>
    public void FlagInconsistent(string reason, DateTime nowUtc)
    {
        IsConsistent = false;
        InconsistencyReason = reason;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Reconstruye las lineas de detalle a partir de la persistencia.</summary>
    public void AttachDetails(IEnumerable<InvoiceDetail> details)
    {
        _details.Clear();
        _details.AddRange(details);
    }

    /// <summary>Reconstruye las notas de credito a partir de la persistencia.</summary>
    public void AttachCreditNotes(IEnumerable<CreditNote> creditNotes)
    {
        _creditNotes.Clear();
        _creditNotes.AddRange(creditNotes);
    }
}
