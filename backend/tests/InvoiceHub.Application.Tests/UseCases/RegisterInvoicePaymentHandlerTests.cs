using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.Ports;
using InvoiceHub.Application.Tests.Fakes;
using InvoiceHub.Application.UseCases;
using InvoiceHub.Domain.Entities;
using InvoiceHub.Domain.Enums;
using InvoiceHub.Domain.Exceptions;

namespace InvoiceHub.Application.Tests.UseCases;

/// <summary>
/// Pruebas del caso de uso de registro de pago. Se cubre la validacion del medio de pago, las reglas de
/// negocio previas (consistencia, factura anulada, pago duplicado) y que el detalle devuelto quede en
/// estado <c>Paid</c> con el pago informado.
/// </summary>
public sealed class RegisterInvoicePaymentHandlerTests
{
    private readonly FakeInvoiceSearchReader _searchReader = new();
    private readonly FakeInvoiceRepository _invoices = new();
    private readonly FakePaymentWritePort _payments = new();
    private readonly FakeStatusRecalculationPort _statuses = new();
    private readonly InMemoryCacheStore _cache = new();
    private readonly FixedClock _clock = FixedClock.Default();

    private RegisterInvoicePaymentHandler CreateHandler()
        => new(_searchReader, _invoices, _payments, _statuses, _cache, _clock);

    [Fact]
    public async Task HandleAsync_WithValidRequest_RegistersPaymentAndReturnsPaidDetail()
    {
        _searchReader.Rows.Add(TestRows.Row(number: 1000, invoiceId: 10, paymentStatus: PaymentStatus.Overdue, daysOverdue: 20));

        // La rutina SQL escribe el pago; el repositorio simula ese efecto al reconsultar el agregado.
        _invoices.Invoices.Add(TestRows.Invoice(
            number: 1000,
            payment: InvoicePayment.Create("Transferencia", TestRows.Today, TestRows.NowUtc)));

        var result = await CreateHandler().HandleAsync(
            1000,
            new RegisterPaymentRequest { PaymentMethod = "  Transferencia  " },
            CancellationToken.None);

        Assert.Equal(PaymentStatus.Paid, result.PaymentStatus);
        Assert.Equal(InvoiceStatus.Issued, result.InvoiceStatus);
        Assert.NotNull(result.Payment);
        Assert.Equal("Transferencia", result.Payment!.PaymentMethod);
        Assert.Equal(TestRows.Today, result.Payment.PaymentDate);

        var request = Assert.Single(_payments.PaymentRequests);
        Assert.Equal(10L, request.InvoiceId);
        Assert.Equal("Transferencia", request.PaymentMethod);
        Assert.Equal(_clock.Today, request.PaymentDate);

        Assert.Equal(new[] { 10L }, _statuses.RecalculatedInvoiceIds);
        Assert.Equal(
            new[] { CacheKeys.InvoicePrefix, CacheKeys.ReportPrefix, CacheKeys.DashboardPrefix },
            _cache.InvalidatedPrefixes);
    }

    [Fact]
    public async Task HandleAsync_WithExplicitDate_UsesProvidedDate()
    {
        var paymentDate = new DateOnly(2026, 1, 5);
        _searchReader.Rows.Add(TestRows.Row());
        _invoices.Invoices.Add(TestRows.Invoice());

        await CreateHandler().HandleAsync(
            1000,
            new RegisterPaymentRequest { PaymentMethod = "Efectivo", PaymentDate = paymentDate },
            CancellationToken.None);

        Assert.Equal(paymentDate, Assert.Single(_payments.PaymentRequests).PaymentDate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task HandleAsync_WithoutPaymentMethod_ThrowsValidationFailed(string? method)
    {
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            CreateHandler().HandleAsync(
                1000,
                new RegisterPaymentRequest { PaymentMethod = method },
                CancellationToken.None));

        Assert.Contains(nameof(RegisterPaymentRequest.PaymentMethod), exception.Errors.Keys);
        Assert.Empty(_payments.PaymentRequests);
    }

    [Fact]
    public async Task HandleAsync_WithTooLongPaymentMethod_ThrowsValidationFailed()
    {
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            CreateHandler().HandleAsync(
                1000,
                new RegisterPaymentRequest { PaymentMethod = new string('x', 61) },
                CancellationToken.None));

        Assert.Contains(nameof(RegisterPaymentRequest.PaymentMethod), exception.Errors.Keys);
    }

    [Fact]
    public async Task HandleAsync_WhenInvoiceDoesNotExist_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateHandler().HandleAsync(
                9999,
                new RegisterPaymentRequest { PaymentMethod = "Transferencia" },
                CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_WhenInvoiceIsInconsistent_ThrowsBusinessRule()
    {
        _searchReader.Rows.Add(TestRows.Row(isConsistent: false, inconsistencyReason: "desviacion de montos"));

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateHandler().HandleAsync(
                1000,
                new RegisterPaymentRequest { PaymentMethod = "Transferencia" },
                CancellationToken.None));

        Assert.Equal(DomainErrorCode.InconsistentInvoice, exception.Code);
        Assert.Empty(_payments.PaymentRequests);
    }

    [Fact]
    public async Task HandleAsync_WhenInvoiceIsCancelled_ThrowsBusinessRule()
    {
        _searchReader.Rows.Add(TestRows.Row(invoiceStatus: InvoiceStatus.Cancelled, outstandingBalanceUnits: 0));

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateHandler().HandleAsync(
                1000,
                new RegisterPaymentRequest { PaymentMethod = "Transferencia" },
                CancellationToken.None));

        Assert.Equal(DomainErrorCode.InvoiceAlreadyCancelled, exception.Code);
        Assert.Empty(_payments.PaymentRequests);
    }

    [Fact]
    public async Task HandleAsync_WhenInvoiceAlreadyHasPayment_ThrowsConflict()
    {
        _searchReader.Rows.Add(TestRows.Row(hasPayment: true, paymentMethod: "Efectivo", paymentDate: TestRows.Today));

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            CreateHandler().HandleAsync(
                1000,
                new RegisterPaymentRequest { PaymentMethod = "Transferencia" },
                CancellationToken.None));

        Assert.Contains("ya tiene un pago registrado", exception.Message);
        Assert.Empty(_payments.PaymentRequests);
    }

    [Theory]
    [InlineData(PaymentAppendOutcome.InvoiceNotFound)]
    [InlineData(PaymentAppendOutcome.InvoiceInconsistent)]
    [InlineData(PaymentAppendOutcome.InvoiceCancelled)]
    [InlineData(PaymentAppendOutcome.AlreadyPaid)]
    public async Task HandleAsync_WhenRoutineRejectsTheWrite_TranslatesTheOutcome(PaymentAppendOutcome outcome)
    {
        _searchReader.Rows.Add(TestRows.Row());
        _payments.PaymentOutcome = outcome;

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            CreateHandler().HandleAsync(
                1000,
                new RegisterPaymentRequest { PaymentMethod = "Transferencia" },
                CancellationToken.None));

        switch (outcome)
        {
            case PaymentAppendOutcome.InvoiceNotFound:
                Assert.IsType<NotFoundException>(exception);
                break;
            case PaymentAppendOutcome.InvoiceInconsistent:
                Assert.Equal(DomainErrorCode.InconsistentInvoice, Assert.IsType<BusinessRuleException>(exception).Code);
                break;
            case PaymentAppendOutcome.InvoiceCancelled:
                Assert.Equal(DomainErrorCode.InvoiceAlreadyCancelled, Assert.IsType<BusinessRuleException>(exception).Code);
                break;
            default:
                Assert.IsType<ConflictException>(exception);
                break;
        }

        Assert.Empty(_cache.InvalidatedPrefixes);
    }
}
