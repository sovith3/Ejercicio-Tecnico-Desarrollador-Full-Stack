using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.Mapping;
using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Enums;
using InvoiceHub.Domain.Exceptions;

namespace InvoiceHub.Application.UseCases;

/// <summary>
/// Caso de uso: emitir una nota de credito contra una factura del sistema activo.
/// <para>
/// El flujo delega la escritura a la rutina de base de datos <c>sp_AppendCreditNote</c> equivalente,
/// que valida de forma atomica (dentro de la misma sentencia) que el monto no supere el saldo
/// pendiente. Antes de invocarla se aplican las reglas del dominio sobre el estado ya publicado
/// (factura consistente, no cancelada, monto positivo y dentro del saldo) para entregar mensajes de
/// negocio precisos; la rutina sigue siendo la ultima barrera ante solicitudes concurrentes.
/// </para>
/// </summary>
public sealed class AddCreditNoteHandler
{
    private readonly IInvoiceSearchReader _searchReader;
    private readonly IInvoiceRepository _invoices;
    private readonly ICreditNoteWritePort _creditNotes;
    private readonly IStatusRecalculationPort _statuses;
    private readonly ICacheStore _cache;
    private readonly IClock _clock;

    public AddCreditNoteHandler(
        IInvoiceSearchReader searchReader,
        IInvoiceRepository invoices,
        ICreditNoteWritePort creditNotes,
        IStatusRecalculationPort statuses,
        ICacheStore cache,
        IClock clock)
    {
        _searchReader = searchReader;
        _invoices = invoices;
        _creditNotes = creditNotes;
        _statuses = statuses;
        _cache = cache;
        _clock = clock;
    }

    public async Task<CreditNoteResultDto> HandleAsync(
        int invoiceNumber,
        CreateCreditNoteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var amount = ValidateAmount(request);

        var invoice = await _searchReader.GetByNumberAsync(invoiceNumber, cancellationToken)
            ?? throw NotFoundException.ForInvoice(invoiceNumber);

        if (!invoice.IsConsistent)
        {
            throw BusinessRuleException.InconsistentInvoice(
                invoiceNumber,
                invoice.InconsistencyReason ?? "desviacion entre el total declarado y el detalle");
        }

        if (invoice.InvoiceStatus == InvoiceStatus.Cancelled)
        {
            throw BusinessRuleException.AlreadyCancelled(invoiceNumber);
        }

        var outstanding = Money.FromUnits(invoice.OutstandingBalanceUnits);
        if (amount > outstanding)
        {
            throw BusinessRuleException.CreditNoteExceedsOutstanding(amount, outstanding, invoiceNumber);
        }

        var creditNoteNumber = request.Number ?? await _invoices.GetNextCreditNoteNumberAsync(cancellationToken);
        var creditNoteDate = request.CreditNoteDate ?? _clock.Today;
        var now = _clock.UtcNow;

        var outcome = await _creditNotes.AppendCreditNoteAsync(
            new CreditNoteAppendRequest(
                invoice.InvoiceId,
                creditNoteNumber,
                creditNoteDate,
                Money.ToUnits(amount),
                now),
            cancellationToken);

        MapOutcome(outcome, invoiceNumber, amount, outstanding);

        await _statuses.RecalculateInvoiceStatusesAsync(invoice.InvoiceId, cancellationToken);
        await _cache.InvalidateInvoiceReadModelsAsync(cancellationToken);

        var refreshed = await _searchReader.GetByNumberAsync(invoiceNumber, cancellationToken)
            ?? throw NotFoundException.ForInvoice(invoiceNumber);

        return new CreditNoteResultDto(
            refreshed.Number,
            creditNoteNumber,
            amount,
            Money.FromUnits(refreshed.CreditNotesTotalUnits),
            Money.FromUnits(refreshed.OutstandingBalanceUnits),
            refreshed.InvoiceStatus,
            refreshed.PaymentStatus,
            now);
    }

    private static decimal ValidateAmount(CreateCreditNoteRequest request)
    {
        if (request.Amount is null)
        {
            throw new ValidationFailedException(nameof(request.Amount), "El monto de la nota de credito es obligatorio.");
        }

        var amount = request.Amount.Value;
        if (amount <= 0m)
        {
            throw BusinessRuleException.CreditNoteAmountNotPositive(amount);
        }

        var rounded = decimal.Round(amount, Money.Decimals, MidpointRounding.AwayFromZero);
        if (rounded != amount)
        {
            throw new ValidationFailedException(
                nameof(request.Amount),
                $"El monto de la nota de credito admite como maximo {Money.Decimals} decimales.");
        }

        return rounded;
    }

    private static void MapOutcome(
        CreditNoteAppendOutcome outcome,
        int invoiceNumber,
        decimal amount,
        decimal outstanding)
    {
        switch (outcome)
        {
            case CreditNoteAppendOutcome.Applied:
                return;
            case CreditNoteAppendOutcome.InvoiceNotFound:
                throw NotFoundException.ForInvoice(invoiceNumber);
            case CreditNoteAppendOutcome.InvoiceInconsistent:
                throw BusinessRuleException.InconsistentInvoice(invoiceNumber, "la rutina de base de datos la marco como inconsistente.");
            case CreditNoteAppendOutcome.InvoiceCancelled:
                throw BusinessRuleException.AlreadyCancelled(invoiceNumber);
            case CreditNoteAppendOutcome.ExceedsOutstandingBalance:
                throw BusinessRuleException.CreditNoteExceedsOutstanding(amount, outstanding, invoiceNumber);
            case CreditNoteAppendOutcome.DuplicatedNumber:
                throw new ConflictException(
                    "El numero de nota de credito ya existe. Informe otro numero o deje el campo vacio para que el sistema lo asigne.");
            default:
                throw new ConflictException("No fue posible registrar la nota de credito.");
        }
    }
}
