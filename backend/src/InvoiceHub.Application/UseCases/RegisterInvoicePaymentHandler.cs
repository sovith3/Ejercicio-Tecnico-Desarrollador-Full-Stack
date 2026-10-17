using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.Mapping;
using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Enums;
using InvoiceHub.Domain.Exceptions;

namespace InvoiceHub.Application.UseCases;

/// <summary>
/// Caso de uso: registrar el pago de una factura. El pago y el nuevo estado de cobro se escriben en la
/// base de datos mediante la rutina correspondiente, garantizando que una factura cancelada o ya
/// pagada no pueda recibir un segundo pago aun con solicitudes concurrentes.
/// </summary>
public sealed class RegisterInvoicePaymentHandler
{
    private readonly IInvoiceSearchReader _searchReader;
    private readonly IInvoiceRepository _invoices;
    private readonly IPaymentWritePort _payments;
    private readonly IStatusRecalculationPort _statuses;
    private readonly ICacheStore _cache;
    private readonly IClock _clock;

    public RegisterInvoicePaymentHandler(
        IInvoiceSearchReader searchReader,
        IInvoiceRepository invoices,
        IPaymentWritePort payments,
        IStatusRecalculationPort statuses,
        ICacheStore cache,
        IClock clock)
    {
        _searchReader = searchReader;
        _invoices = invoices;
        _payments = payments;
        _statuses = statuses;
        _cache = cache;
        _clock = clock;
    }

    public async Task<InvoiceDetailDto> HandleAsync(
        int invoiceNumber,
        RegisterPaymentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var paymentMethod = request.PaymentMethod?.Trim();
        if (string.IsNullOrWhiteSpace(paymentMethod))
        {
            throw new ValidationFailedException(nameof(request.PaymentMethod), "El medio de pago es obligatorio.");
        }

        if (paymentMethod.Length > 60)
        {
            throw new ValidationFailedException(
                nameof(request.PaymentMethod),
                "El medio de pago no puede superar los 60 caracteres.");
        }

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

        if (invoice.HasPayment)
        {
            throw new ConflictException($"La factura {invoiceNumber} ya tiene un pago registrado.");
        }

        var paymentDate = request.PaymentDate ?? _clock.Today;
        var now = _clock.UtcNow;

        var outcome = await _payments.RegisterPaymentAsync(
            new PaymentAppendRequest(invoice.InvoiceId, paymentMethod, paymentDate, now),
            cancellationToken);

        switch (outcome)
        {
            case PaymentAppendOutcome.Applied:
                break;
            case PaymentAppendOutcome.InvoiceNotFound:
                throw NotFoundException.ForInvoice(invoiceNumber);
            case PaymentAppendOutcome.InvoiceInconsistent:
                throw BusinessRuleException.InconsistentInvoice(invoiceNumber, "la rutina de base de datos la marco como inconsistente.");
            case PaymentAppendOutcome.InvoiceCancelled:
                throw BusinessRuleException.AlreadyCancelled(invoiceNumber);
            case PaymentAppendOutcome.AlreadyPaid:
                throw new ConflictException($"La factura {invoiceNumber} ya tiene un pago registrado.");
            default:
                throw new ConflictException("No fue posible registrar el pago.");
        }

        await _statuses.RecalculateInvoiceStatusesAsync(invoice.InvoiceId, cancellationToken);
        await _cache.InvalidateInvoiceReadModelsAsync(cancellationToken);

        var refreshed = await _invoices.GetByNumberAsync(invoiceNumber, cancellationToken)
            ?? throw NotFoundException.ForInvoice(invoiceNumber);

        refreshed.RefreshStatuses(_clock.Today);

        return InvoiceDtoMapper.ToDetail(refreshed, _clock.Today);
    }
}
