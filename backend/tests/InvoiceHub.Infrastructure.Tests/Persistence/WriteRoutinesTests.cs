using InvoiceHub.Application.Common;
using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceHub.Infrastructure.Tests.Persistence;

/// <summary>
/// Pruebas de las rutinas de escritura ejecutadas en el motor SQLite (nota de credito y pago). Se comprueba
/// que la validacion ocurra dentro de la propia sentencia (saldo pendiente, numero duplicado, factura
/// anulada o inconsistente), que los rechazos se traduzcan al ordinal del enum esperado por la capa de
/// aplicacion y que el recalculo de estados publique el resultado en la vista de busqueda.
/// </summary>
public sealed class WriteRoutinesTests : IAsyncLifetime
{
    private readonly TestHost _host = new();

    public async Task InitializeAsync() => await _host.InitializeAsync();

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    private Task<T> WithCreditNotesAsync<T>(Func<ICreditNoteWritePort, Task<T>> action)
        => _host.WithScopeAsync(provider => action(provider.GetRequiredService<ICreditNoteWritePort>()));

    private Task<T> WithPaymentsAsync<T>(Func<IPaymentWritePort, Task<T>> action)
        => _host.WithScopeAsync(provider => action(provider.GetRequiredService<IPaymentWritePort>()));

    private Task<T> WithStatusesAsync<T>(Func<IStatusRecalculationPort, Task<T>> action)
        => _host.WithScopeAsync(provider => action(provider.GetRequiredService<IStatusRecalculationPort>()));

    private Task<T> WithReaderAsync<T>(Func<IInvoiceSearchReader, Task<T>> action)
        => _host.WithScopeAsync(provider => action(provider.GetRequiredService<IInvoiceSearchReader>()));

    private static CreditNoteAppendRequest CreditNote(long invoiceId, int number, decimal amount)
        => new(invoiceId, number, TestData.Today, Money.ToUnits(amount), TestData.NowUtc);

    private static PaymentAppendRequest Payment(long invoiceId, string method = "Transferencia")
        => new(invoiceId, method, TestData.Today, TestData.NowUtc);

    [Fact]
    public async Task AppendCreditNote_WhenValid_InsertsTheNoteAndReducesTheBalance()
    {
        var invoiceId = await _host.AddInvoiceAsync(TestData.Invoice(number: 6001));

        var outcome = await WithCreditNotesAsync(creditNotes =>
            creditNotes.AppendCreditNoteAsync(CreditNote(invoiceId, 9001, 500m), CancellationToken.None));

        Assert.Equal(CreditNoteAppendOutcome.Applied, outcome);

        await WithStatusesAsync(statuses => statuses.RecalculateInvoiceStatusesAsync(invoiceId, CancellationToken.None));

        var row = await WithReaderAsync(reader => reader.GetByNumberAsync(6001, CancellationToken.None));

        Assert.NotNull(row);
        Assert.Equal(InvoiceStatus.Partial, row!.InvoiceStatus);
        Assert.Equal(Money.ToUnits(500m), row.CreditNotesTotalUnits);
        Assert.Equal(Money.ToUnits(1500m), row.OutstandingBalanceUnits);
        Assert.Equal(1, row.CreditNoteCount);
    }

    [Fact]
    public async Task AppendCreditNote_WhenAmountEqualsBalance_CancelsTheInvoice()
    {
        var invoiceId = await _host.AddInvoiceAsync(TestData.Invoice(number: 6002));

        var outcome = await WithCreditNotesAsync(creditNotes =>
            creditNotes.AppendCreditNoteAsync(CreditNote(invoiceId, 9002, 2000m), CancellationToken.None));

        await WithStatusesAsync(statuses => statuses.RecalculateInvoiceStatusesAsync(invoiceId, CancellationToken.None));

        var row = await WithReaderAsync(reader => reader.GetByNumberAsync(6002, CancellationToken.None));

        Assert.Equal(CreditNoteAppendOutcome.Applied, outcome);
        Assert.Equal(InvoiceStatus.Cancelled, row!.InvoiceStatus);
        Assert.Equal(0, row.OutstandingBalanceUnits);
    }

    [Fact]
    public async Task AppendCreditNote_WhenAmountExceedsTheBalance_IsRejectedWithoutInserting()
    {
        var invoiceId = await _host.AddInvoiceAsync(TestData.Invoice(number: 6003));

        var outcome = await WithCreditNotesAsync(creditNotes =>
            creditNotes.AppendCreditNoteAsync(CreditNote(invoiceId, 9003, 2500m), CancellationToken.None));

        var notes = await _host.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM credit_notes;");

        Assert.Equal(CreditNoteAppendOutcome.ExceedsOutstandingBalance, outcome);
        Assert.Equal(0L, notes);
    }

    [Fact]
    public async Task AppendCreditNote_WithDuplicatedNumber_IsRejected()
    {
        var invoiceId = await _host.AddInvoiceAsync(TestData.Invoice(number: 6004));

        await WithCreditNotesAsync(creditNotes =>
            creditNotes.AppendCreditNoteAsync(CreditNote(invoiceId, 9004, 100m), CancellationToken.None));

        var outcome = await WithCreditNotesAsync(creditNotes =>
            creditNotes.AppendCreditNoteAsync(CreditNote(invoiceId, 9004, 100m), CancellationToken.None));

        Assert.Equal(CreditNoteAppendOutcome.DuplicatedNumber, outcome);
    }

    [Fact]
    public async Task AppendCreditNote_ForUnknownInvoice_IsRejected()
    {
        var outcome = await WithCreditNotesAsync(creditNotes =>
            creditNotes.AppendCreditNoteAsync(CreditNote(999_999, 9005, 10m), CancellationToken.None));

        Assert.Equal(CreditNoteAppendOutcome.InvoiceNotFound, outcome);
    }

    [Fact]
    public async Task AppendCreditNote_OnCancelledInvoice_IsRejected()
    {
        var invoiceId = await _host.AddInvoiceAsync(TestData.Invoice(number: 6005));

        await WithCreditNotesAsync(creditNotes =>
            creditNotes.AppendCreditNoteAsync(CreditNote(invoiceId, 9006, 2000m), CancellationToken.None));
        await WithStatusesAsync(statuses => statuses.RecalculateInvoiceStatusesAsync(invoiceId, CancellationToken.None));

        var outcome = await WithCreditNotesAsync(creditNotes =>
            creditNotes.AppendCreditNoteAsync(CreditNote(invoiceId, 9007, 1m), CancellationToken.None));

        Assert.Equal(CreditNoteAppendOutcome.InvoiceCancelled, outcome);
    }

    [Fact]
    public async Task AppendCreditNote_OnInconsistentInvoice_IsRejected()
    {
        var inconsistent = TestData.Invoice(
            number: 6006,
            totalAmount: 2000m,
            details: new[] { TestData.Line(unitPrice: 100m, quantity: 1) });
        var invoiceId = await _host.AddInvoiceAsync(inconsistent);

        var outcome = await WithCreditNotesAsync(creditNotes =>
            creditNotes.AppendCreditNoteAsync(CreditNote(invoiceId, 9008, 10m), CancellationToken.None));

        Assert.False(inconsistent.IsConsistent);
        Assert.Equal(CreditNoteAppendOutcome.InvoiceInconsistent, outcome);
    }

    [Fact]
    public async Task AppendCreditNote_ForConcurrentRequests_OnlyOneSucceeds()
    {
        var invoiceId = await _host.AddInvoiceAsync(TestData.Invoice(number: 6007));

        var outcomes = await Task.WhenAll(
            WithCreditNotesAsync(creditNotes =>
                creditNotes.AppendCreditNoteAsync(CreditNote(invoiceId, 9009, 1500m), CancellationToken.None)),
            WithCreditNotesAsync(creditNotes =>
                creditNotes.AppendCreditNoteAsync(CreditNote(invoiceId, 9010, 1500m), CancellationToken.None)));

        var notes = await _host.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM credit_notes;");

        Assert.Equal(1, outcomes.Count(outcome => outcome == CreditNoteAppendOutcome.Applied));
        Assert.Equal(1L, notes);
    }

    [Fact]
    public async Task RegisterPayment_WhenValid_RegistersItOnceAndPublishesPaidStatus()
    {
        var invoiceId = await _host.AddInvoiceAsync(TestData.Invoice(
            number: 6101,
            paymentDueDate: TestData.Today.AddDays(-40),
            daysToDue: 30));

        var outcome = await WithPaymentsAsync(payments =>
            payments.RegisterPaymentAsync(Payment(invoiceId), CancellationToken.None));
        await WithStatusesAsync(statuses => statuses.RecalculateInvoiceStatusesAsync(invoiceId, CancellationToken.None));

        var row = await WithReaderAsync(reader => reader.GetByNumberAsync(6101, CancellationToken.None));

        Assert.Equal(PaymentAppendOutcome.Applied, outcome);
        Assert.True(row!.HasPayment);
        Assert.Equal("Transferencia", row.PaymentMethod);
        Assert.Equal(TestData.Today, row.PaymentDate);
        Assert.Equal(PaymentStatus.Paid, row.PaymentStatus);
    }

    [Fact]
    public async Task RegisterPayment_Twice_SecondAttemptIsRejected()
    {
        var invoiceId = await _host.AddInvoiceAsync(TestData.Invoice(number: 6102));

        await WithPaymentsAsync(payments => payments.RegisterPaymentAsync(Payment(invoiceId), CancellationToken.None));
        var outcome = await WithPaymentsAsync(payments =>
            payments.RegisterPaymentAsync(Payment(invoiceId), CancellationToken.None));

        var payments = await _host.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM invoice_payments;");

        Assert.Equal(PaymentAppendOutcome.AlreadyPaid, outcome);
        Assert.Equal(1L, payments);
    }

    [Fact]
    public async Task RegisterPayment_OnCancelledInvoice_IsRejected()
    {
        var invoiceId = await _host.AddInvoiceAsync(TestData.Invoice(number: 6103));

        await WithCreditNotesAsync(creditNotes =>
            creditNotes.AppendCreditNoteAsync(CreditNote(invoiceId, 9101, 2000m), CancellationToken.None));
        await WithStatusesAsync(statuses => statuses.RecalculateInvoiceStatusesAsync(invoiceId, CancellationToken.None));

        var outcome = await WithPaymentsAsync(payments =>
            payments.RegisterPaymentAsync(Payment(invoiceId), CancellationToken.None));

        Assert.Equal(PaymentAppendOutcome.InvoiceCancelled, outcome);
    }

    [Fact]
    public async Task RegisterPayment_OnInconsistentInvoice_IsRejected()
    {
        var invoiceId = await _host.AddInvoiceAsync(TestData.Invoice(
            number: 6104,
            totalAmount: 2000m,
            details: new[] { TestData.Line(unitPrice: 100m, quantity: 1) }));

        var outcome = await WithPaymentsAsync(payments =>
            payments.RegisterPaymentAsync(Payment(invoiceId), CancellationToken.None));

        Assert.Equal(PaymentAppendOutcome.InvoiceInconsistent, outcome);
    }

    [Fact]
    public async Task RegisterPayment_ForUnknownInvoice_IsRejected()
    {
        var outcome = await WithPaymentsAsync(payments =>
            payments.RegisterPaymentAsync(Payment(999_999), CancellationToken.None));

        Assert.Equal(PaymentAppendOutcome.InvoiceNotFound, outcome);
    }
}
