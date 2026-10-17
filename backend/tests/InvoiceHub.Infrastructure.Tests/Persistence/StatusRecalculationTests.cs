using System.Globalization;
using InvoiceHub.Application.Common;
using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceHub.Infrastructure.Tests.Persistence;

/// <summary>
/// Pruebas del recalculo de estados y del saldo calculado por el motor. La rutina recibe la fecha de negocio
/// del reloj de la aplicacion (<c>@Today</c>), por lo que las fechas de las pruebas se derivan de la fecha
/// real para que el resultado no dependa del calendario de la maquina.
/// </summary>
public sealed class StatusRecalculationTests : IAsyncLifetime
{
    private static readonly DateOnly RealToday = TestData.BusinessToday();

    private readonly TestHost _host = new();

    public async Task InitializeAsync() => await _host.InitializeAsync();

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    private Task<T> WithStatusesAsync<T>(Func<IStatusRecalculationPort, Task<T>> action)
        => _host.WithScopeAsync(provider => action(provider.GetRequiredService<IStatusRecalculationPort>()));

    private Task<T> WithCreditNotesAsync<T>(Func<ICreditNoteWritePort, Task<T>> action)
        => _host.WithScopeAsync(provider => action(provider.GetRequiredService<ICreditNoteWritePort>()));

    private Task<T> WithReaderAsync<T>(Func<IInvoiceSearchReader, Task<T>> action)
        => _host.WithScopeAsync(provider => action(provider.GetRequiredService<IInvoiceSearchReader>()));

    [Fact]
    public async Task RecalculateInvoiceStatusesAsync_PastDueWithoutPayment_MarksOverdue()
    {
        var invoiceId = await _host.AddInvoiceAsync(TestData.Invoice(
            number: 7001,
            invoiceDate: RealToday.AddDays(-60),
            paymentDueDate: RealToday.AddDays(-30),
            daysToDue: 30));

        var evaluated = await WithStatusesAsync(statuses =>
            statuses.RecalculateInvoiceStatusesAsync(invoiceId, CancellationToken.None));

        var row = await WithReaderAsync(reader => reader.GetByNumberAsync(7001, CancellationToken.None));

        Assert.Equal(1, evaluated);
        Assert.Equal(PaymentStatus.Overdue, row!.PaymentStatus);
        Assert.Equal(InvoiceStatus.Issued, row.InvoiceStatus);
    }

    [Fact]
    public async Task RecalculateInvoiceStatusesAsync_FutureDueDate_MarksPending()
    {
        var invoiceId = await _host.AddInvoiceAsync(TestData.Invoice(
            number: 7002,
            invoiceDate: RealToday,
            paymentDueDate: RealToday.AddDays(30),
            daysToDue: 30));

        await WithStatusesAsync(statuses =>
            statuses.RecalculateInvoiceStatusesAsync(invoiceId, CancellationToken.None));

        var row = await WithReaderAsync(reader => reader.GetByNumberAsync(7002, CancellationToken.None));

        Assert.Equal(PaymentStatus.Pending, row!.PaymentStatus);
    }

    [Fact]
    public async Task RecalculateInvoiceStatusesAsync_WhenDeclaredTotalDiffers_MarksInconsistencyWithReason()
    {
        var invoiceId = await _host.AddInvoiceAsync(TestData.Invoice(
            number: 7003,
            totalAmount: 2000m,
            details: new[] { TestData.Line(unitPrice: 900m, quantity: 2) }));

        await WithStatusesAsync(statuses =>
            statuses.RecalculateInvoiceStatusesAsync(invoiceId, CancellationToken.None));

        var row = await WithReaderAsync(reader => reader.GetByNumberAsync(7003, CancellationToken.None));

        // Las facturas inconsistentes quedan fuera del sistema activo: ni siquiera aparecen en la busqueda.
        Assert.Null(row);

        var isConsistent = await _host.ExecuteScalarAsync<long>(
            $"SELECT IsConsistent FROM invoices WHERE Id = {invoiceId};");
        var reason = await _host.ExecuteScalarAsync<string>(
            $"SELECT InconsistencyReason FROM invoices WHERE Id = {invoiceId};");

        Assert.Equal(0L, isConsistent);
        AssertMentionsAmounts(reason, declared: 2000m, details: 1800m);
    }

    [Fact]
    public async Task RecalculateInvoiceStatusesAsync_WhenDetailsAreCorruptedInTheDatabase_MarksInconsistency()
    {
        // Factura que nace consistente: la divergencia se introduce directamente en la base para verificar que
        // la rutina (y no el dominio) sea capaz de detectarla y describirla.
        var invoiceId = await _host.AddInvoiceAsync(TestData.Invoice(number: 7005, totalAmount: 2000m));

        await _host.ExecuteNonQueryAsync(
            $"UPDATE invoice_details SET SubtotalUnits = 180000000 WHERE InvoiceId = {invoiceId};");

        var evaluated = await WithStatusesAsync(statuses =>
            statuses.RecalculateInvoiceStatusesAsync(invoiceId, CancellationToken.None));

        var isConsistent = await _host.ExecuteScalarAsync<long>(
            $"SELECT IsConsistent FROM invoices WHERE Id = {invoiceId};");
        var reason = await _host.ExecuteScalarAsync<string>(
            $"SELECT InconsistencyReason FROM invoices WHERE Id = {invoiceId};");
        var row = await WithReaderAsync(reader => reader.GetByNumberAsync(7005, CancellationToken.None));

        Assert.Equal(1, evaluated);
        Assert.Equal(0L, isConsistent);
        AssertMentionsAmounts(reason, declared: 2000m, details: 1800m);
        Assert.Null(row);
    }

    [Fact]
    public async Task RecalculateInvoiceStatusesAsync_KeepsPreviouslyFlaggedInvoiceOutsideTheActiveSystem()
    {
        var invoiceId = await _host.AddInvoiceAsync(TestData.Invoice(
            number: 7004,
            totalAmount: 2000m,
            details: new[] { TestData.Line(unitPrice: 900m, quantity: 2) }));

        await WithStatusesAsync(statuses =>
            statuses.RecalculateInvoiceStatusesAsync(invoiceId, CancellationToken.None));

        // Se corrige la linea de detalle: la rutina no reincorpora por si sola una factura ya marcada, porque
        // el origen de datos debe reimportarse (o mediar una accion explicita) para volver al sistema activo.
        await _host.ExecuteNonQueryAsync(
            $"UPDATE invoice_details SET SubtotalUnits = 200000000 WHERE InvoiceId = {invoiceId};");

        await WithStatusesAsync(statuses =>
            statuses.RecalculateInvoiceStatusesAsync(invoiceId, CancellationToken.None));

        var isConsistent = await _host.ExecuteScalarAsync<long>(
            $"SELECT IsConsistent FROM invoices WHERE Id = {invoiceId};");
        var reason = await _host.ExecuteScalarAsync<string>(
            $"SELECT InconsistencyReason FROM invoices WHERE Id = {invoiceId};");
        var row = await WithReaderAsync(reader => reader.GetByNumberAsync(7004, CancellationToken.None));

        Assert.Equal(0L, isConsistent);
        Assert.NotNull(reason);
        Assert.Null(row);
    }

    /// <summary>
    /// Comprueba que el motivo de inconsistencia mencione ambos montos, sin depender de los separadores
    /// usados por el dominio (1.800,00) o por la rutina SQL (1800.00).
    /// </summary>
    private static void AssertMentionsAmounts(string? reason, decimal declared, decimal details)
    {
        Assert.NotNull(reason);

        var digitsOnly = reason!.Replace(".", string.Empty).Replace(",", string.Empty);

        foreach (var amount in new[] { declared, details })
        {
            var expected = amount.ToString("0", CultureInfo.InvariantCulture);

            Assert.True(
                digitsOnly.Contains(expected, StringComparison.Ordinal),
                $"El motivo no menciona el monto {amount}: {reason}");
        }
    }

    [Fact]
    public async Task RecalculateAllStatusesAsync_EvaluatesEveryInvoice()
    {
        await _host.AddInvoiceAsync(TestData.Invoice(
            number: 7101,
            invoiceDate: RealToday.AddDays(-45),
            paymentDueDate: RealToday.AddDays(-15),
            daysToDue: 30));
        await _host.AddInvoiceAsync(TestData.Invoice(
            number: 7102,
            invoiceDate: RealToday,
            paymentDueDate: RealToday.AddDays(20),
            daysToDue: 20));

        var evaluated = await WithStatusesAsync(statuses =>
            statuses.RecalculateAllStatusesAsync(CancellationToken.None));

        var overdue = await WithReaderAsync(reader => reader.GetByNumberAsync(7101, CancellationToken.None));
        var pending = await WithReaderAsync(reader => reader.GetByNumberAsync(7102, CancellationToken.None));

        Assert.Equal(2, evaluated);
        Assert.Equal(PaymentStatus.Overdue, overdue!.PaymentStatus);
        Assert.Equal(PaymentStatus.Pending, pending!.PaymentStatus);
    }

    [Fact]
    public async Task RecalculateAllStatusesAsync_WhenNoInvoicesExist_ReturnsZero()
    {
        var evaluated = await WithStatusesAsync(statuses =>
            statuses.RecalculateAllStatusesAsync(CancellationToken.None));

        Assert.Equal(0, evaluated);
    }

    [Fact]
    public async Task GetOutstandingBalanceUnitsAsync_ReturnsTheEngineBalance()
    {
        var invoiceId = await _host.AddInvoiceAsync(TestData.Invoice(number: 7201));

        var initial = await WithCreditNotesAsync(creditNotes =>
            creditNotes.GetOutstandingBalanceUnitsAsync(invoiceId, CancellationToken.None));

        await WithCreditNotesAsync(creditNotes => creditNotes.AppendCreditNoteAsync(
            new CreditNoteAppendRequest(invoiceId, 9201, TestData.Today, Money.ToUnits(1200m), TestData.NowUtc),
            CancellationToken.None));

        var afterCreditNote = await WithCreditNotesAsync(creditNotes =>
            creditNotes.GetOutstandingBalanceUnitsAsync(invoiceId, CancellationToken.None));

        Assert.Equal(Money.ToUnits(2000m), initial);
        Assert.Equal(Money.ToUnits(800m), afterCreditNote);
    }

    [Fact]
    public async Task GetOutstandingBalanceUnitsAsync_ForUnknownInvoice_ReturnsNull()
    {
        var balance = await WithCreditNotesAsync(creditNotes =>
            creditNotes.GetOutstandingBalanceUnitsAsync(999_999, CancellationToken.None));

        Assert.Null(balance);
    }
}
