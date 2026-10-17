using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.Ports;
using InvoiceHub.Application.Tests.Fakes;
using InvoiceHub.Application.UseCases;
using InvoiceHub.Domain.Enums;
using InvoiceHub.Domain.Exceptions;

namespace InvoiceHub.Application.Tests.UseCases;

/// <summary>
/// Pruebas del caso de uso de emision de notas de credito. Se verifican las validaciones previas al
/// motor, la traduccion de los desenlaces de la rutina SQL, la invalidacion de cache y que el contrato
/// de respuesta entregue los estados como valores del dominio (no como textos en espanol).
/// </summary>
public sealed class AddCreditNoteHandlerTests
{
    private readonly FakeInvoiceSearchReader _searchReader = new();
    private readonly FakeInvoiceRepository _invoices = new();
    private readonly FakeCreditNoteWritePort _creditNotes = new();
    private readonly FakeStatusRecalculationPort _statuses = new();
    private readonly InMemoryCacheStore _cache = new();
    private readonly FixedClock _clock = FixedClock.Default();

    private AddCreditNoteHandler CreateHandler()
        => new(_searchReader, _invoices, _creditNotes, _statuses, _cache, _clock);

    [Fact]
    public async Task HandleAsync_WithValidRequest_ReturnsDomainStatusesAndWritesThroughRoutine()
    {
        var before = TestRows.Row(number: 1000, invoiceId: 10);
        var after = TestRows.Row(
            number: 1000,
            invoiceId: 10,
            creditNotesTotalUnits: 50_000_000,
            outstandingBalanceUnits: 150_000_000,
            creditNoteCount: 1,
            invoiceStatus: InvoiceStatus.Partial);

        _searchReader.GetByNumberSequence.Add(before);
        _searchReader.GetByNumberSequence.Add(after);

        var request = new CreateCreditNoteRequest { Number = 7001, Amount = 500m };

        var result = await CreateHandler().HandleAsync(1000, request, CancellationToken.None);

        Assert.Equal(1000, result.InvoiceNumber);
        Assert.Equal(7001, result.CreditNoteNumber);
        Assert.Equal(500m, result.CreditNoteAmount);
        Assert.Equal(500m, result.CreditNotesTotal);
        Assert.Equal(1500m, result.OutstandingBalance);
        Assert.Equal(InvoiceStatus.Partial, result.InvoiceStatus);
        Assert.Equal(PaymentStatus.Pending, result.PaymentStatus);
        Assert.Equal(_clock.UtcNow, result.CreatedAtUtc);

        var appended = Assert.Single(_creditNotes.CreditNoteRequests);
        Assert.Equal(10L, appended.InvoiceId);
        Assert.Equal(7001, appended.Number);
        Assert.Equal(50_000_000L, appended.AmountUnits);
        Assert.Equal(_clock.Today, appended.CreditNoteDate);
        Assert.Equal(_clock.UtcNow, appended.CreatedAtUtc);

        Assert.Equal(new[] { 10L }, _statuses.RecalculatedInvoiceIds);
        Assert.Equal(
            new[] { CacheKeys.InvoicePrefix, CacheKeys.ReportPrefix, CacheKeys.DashboardPrefix },
            _cache.InvalidatedPrefixes);
    }

    [Fact]
    public async Task HandleAsync_WithoutNumber_AsksRepositoryForNextCorrelative()
    {
        _invoices.NextCreditNoteNumber = 8005;
        _searchReader.GetByNumberSequence.Add(TestRows.Row());
        _searchReader.GetByNumberSequence.Add(TestRows.Row(
            creditNotesTotalUnits: 10_000_000,
            outstandingBalanceUnits: 190_000_000,
            invoiceStatus: InvoiceStatus.Partial));

        var result = await CreateHandler().HandleAsync(
            1000,
            new CreateCreditNoteRequest { Amount = 100m },
            CancellationToken.None);

        Assert.Equal(8005, result.CreditNoteNumber);
        Assert.Equal(8005, Assert.Single(_creditNotes.CreditNoteRequests).Number);
    }

    [Fact]
    public async Task HandleAsync_WithExplicitDate_UsesProvidedDate()
    {
        var creditNoteDate = new DateOnly(2026, 1, 2);
        _searchReader.GetByNumberSequence.Add(TestRows.Row());
        _searchReader.GetByNumberSequence.Add(TestRows.Row());

        await CreateHandler().HandleAsync(
            1000,
            new CreateCreditNoteRequest { Amount = 100m, CreditNoteDate = creditNoteDate },
            CancellationToken.None);

        Assert.Equal(creditNoteDate, Assert.Single(_creditNotes.CreditNoteRequests).CreditNoteDate);
    }

    [Fact]
    public async Task HandleAsync_WithoutAmount_ThrowsValidationFailed()
    {
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            CreateHandler().HandleAsync(1000, new CreateCreditNoteRequest(), CancellationToken.None));

        Assert.Contains(nameof(CreateCreditNoteRequest.Amount), exception.Errors.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task HandleAsync_WithNonPositiveAmount_ThrowsBusinessRule(decimal amount)
    {
        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateHandler().HandleAsync(
                1000,
                new CreateCreditNoteRequest { Amount = amount },
                CancellationToken.None));

        Assert.Equal(DomainErrorCode.CreditNoteAmountNotPositive, exception.Code);
    }

    [Fact]
    public async Task HandleAsync_WithMoreThanFiveDecimals_ThrowsValidationFailed()
    {
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            CreateHandler().HandleAsync(
                1000,
                new CreateCreditNoteRequest { Amount = 100.0000001m },
                CancellationToken.None));

        Assert.Contains(nameof(CreateCreditNoteRequest.Amount), exception.Errors.Keys);
    }

    [Fact]
    public async Task HandleAsync_WithFiveDecimals_PreservesThePrecisionInUnits()
    {
        _searchReader.GetByNumberSequence.Add(TestRows.Row(number: 1000, invoiceId: 10));
        _searchReader.GetByNumberSequence.Add(TestRows.Row(
            number: 1000,
            invoiceId: 10,
            creditNotesTotalUnits: 1,
            outstandingBalanceUnits: 199_999_999,
            creditNoteCount: 1,
            invoiceStatus: InvoiceStatus.Partial));

        var result = await CreateHandler().HandleAsync(
            1000,
            new CreateCreditNoteRequest { Number = 7002, Amount = 0.00001m },
            CancellationToken.None);

        Assert.Equal(0.00001m, result.CreditNoteAmount);
        Assert.Equal(1L, Assert.Single(_creditNotes.CreditNoteRequests).AmountUnits);
    }

    [Fact]
    public async Task HandleAsync_WhenInvoiceDoesNotExist_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateHandler().HandleAsync(4321, new CreateCreditNoteRequest { Amount = 10m }, CancellationToken.None));

        Assert.Empty(_creditNotes.CreditNoteRequests);
    }

    [Fact]
    public async Task HandleAsync_WhenInvoiceIsInconsistent_ThrowsBusinessRuleWithoutWriting()
    {
        _searchReader.Rows.Add(TestRows.Row(
            isConsistent: false,
            inconsistencyReason: "total declarado distinto del detalle"));

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateHandler().HandleAsync(1000, new CreateCreditNoteRequest { Amount = 10m }, CancellationToken.None));

        Assert.Equal(DomainErrorCode.InconsistentInvoice, exception.Code);
        Assert.Contains("total declarado distinto del detalle", exception.Message);
        Assert.Empty(_creditNotes.CreditNoteRequests);
    }

    [Fact]
    public async Task HandleAsync_WhenInvoiceIsCancelled_ThrowsAlreadyCancelled()
    {
        _searchReader.Rows.Add(TestRows.Row(invoiceStatus: InvoiceStatus.Cancelled, outstandingBalanceUnits: 0));

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateHandler().HandleAsync(1000, new CreateCreditNoteRequest { Amount = 10m }, CancellationToken.None));

        Assert.Equal(DomainErrorCode.InvoiceAlreadyCancelled, exception.Code);
        Assert.Empty(_creditNotes.CreditNoteRequests);
    }

    [Fact]
    public async Task HandleAsync_WhenAmountExceedsOutstanding_ThrowsBusinessRuleAndSkipsRoutine()
    {
        _searchReader.Rows.Add(TestRows.Row(
            creditNotesTotalUnits: 100_000_000,
            outstandingBalanceUnits: 100_000_000,
            invoiceStatus: InvoiceStatus.Partial));

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreateHandler().HandleAsync(1000, new CreateCreditNoteRequest { Amount = 1000.01m }, CancellationToken.None));

        Assert.Equal(DomainErrorCode.CreditNoteExceedsOutstandingBalance, exception.Code);
        Assert.Empty(_creditNotes.CreditNoteRequests);
        Assert.Empty(_cache.InvalidatedPrefixes);
    }

    [Fact]
    public async Task HandleAsync_WhenAmountEqualsOutstanding_IsAcceptedAndCancelsInvoice()
    {
        _searchReader.GetByNumberSequence.Add(TestRows.Row(
            outstandingBalanceUnits: 100_000_000,
            invoiceStatus: InvoiceStatus.Partial));
        _searchReader.GetByNumberSequence.Add(TestRows.Row(
            creditNotesTotalUnits: 200_000_000,
            outstandingBalanceUnits: 0,
            creditNoteCount: 1,
            invoiceStatus: InvoiceStatus.Cancelled));

        var result = await CreateHandler().HandleAsync(
            1000,
            new CreateCreditNoteRequest { Amount = 1000m },
            CancellationToken.None);

        Assert.Equal(1000m, result.CreditNoteAmount);
        Assert.Equal(0m, result.OutstandingBalance);
        Assert.Equal(InvoiceStatus.Cancelled, result.InvoiceStatus);
    }

    [Theory]
    [InlineData(CreditNoteAppendOutcome.InvoiceNotFound)]
    [InlineData(CreditNoteAppendOutcome.InvoiceInconsistent)]
    [InlineData(CreditNoteAppendOutcome.InvoiceCancelled)]
    [InlineData(CreditNoteAppendOutcome.ExceedsOutstandingBalance)]
    [InlineData(CreditNoteAppendOutcome.DuplicatedNumber)]
    public async Task HandleAsync_WhenRoutineRejectsTheWrite_TranslatesTheOutcome(CreditNoteAppendOutcome outcome)
    {
        _searchReader.Rows.Add(TestRows.Row());
        _creditNotes.CreditNoteOutcome = outcome;

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            CreateHandler().HandleAsync(1000, new CreateCreditNoteRequest { Amount = 100m }, CancellationToken.None));

        switch (outcome)
        {
            case CreditNoteAppendOutcome.InvoiceNotFound:
                Assert.IsType<NotFoundException>(exception);
                break;
            case CreditNoteAppendOutcome.InvoiceInconsistent:
                Assert.Equal(DomainErrorCode.InconsistentInvoice, Assert.IsType<BusinessRuleException>(exception).Code);
                break;
            case CreditNoteAppendOutcome.InvoiceCancelled:
                Assert.Equal(DomainErrorCode.InvoiceAlreadyCancelled, Assert.IsType<BusinessRuleException>(exception).Code);
                break;
            case CreditNoteAppendOutcome.ExceedsOutstandingBalance:
                Assert.Equal(
                    DomainErrorCode.CreditNoteExceedsOutstandingBalance,
                    Assert.IsType<BusinessRuleException>(exception).Code);
                break;
            default:
                Assert.IsType<ConflictException>(exception);
                break;
        }

        Assert.Empty(_cache.InvalidatedPrefixes);
        Assert.Empty(_statuses.RecalculatedInvoiceIds);
    }
}
