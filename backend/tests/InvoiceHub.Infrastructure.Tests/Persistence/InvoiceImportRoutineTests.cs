using InvoiceHub.Application.Common;
using InvoiceHub.Application.Mapping;
using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Entities;
using InvoiceHub.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceHub.Infrastructure.Tests.Persistence;

/// <summary>
/// Pruebas de la rutina de carga masiva (staging del lote en JSON + merge set-based). Verifican que el lote
/// se aplique completo dentro del motor (cliente, factura, detalle, nota de credito y pago), que la
/// deteccion de numeros ya persistidos o repetidos dentro del lote sea idempotente y que el desenlace
/// devuelto conserve el orden de los registros enviados.
/// </summary>
public sealed class InvoiceImportRoutineTests : IAsyncLifetime
{
    private readonly TestHost _host = new();

    public async Task InitializeAsync() => await _host.InitializeAsync();

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    private Task<T> WithImportAsync<T>(Func<IInvoiceImportPort, Task<T>> action)
        => _host.WithScopeAsync(provider => action(provider.GetRequiredService<IInvoiceImportPort>()));

    private Task<T> WithReaderAsync<T>(Func<IInvoiceSearchReader, Task<T>> action)
        => _host.WithScopeAsync(provider => action(provider.GetRequiredService<IInvoiceSearchReader>()));

    /// <summary>Traduce los agregados al contrato del puerto y ejecuta la carga del lote completo.</summary>
    private Task<IReadOnlyList<InvoiceImportOutcome>> ImportAsync(params Invoice[] invoices)
        => WithImportAsync(port => port.ImportBatchAsync(
            invoices.Select(InvoicePersistMapper.ToRecord).ToList(),
            CancellationToken.None));

    private static InvoicePayment Payment()
        => InvoicePayment.Create("Transferencia", TestData.Today, TestData.NowUtc);

    [Fact]
    public async Task ImportBatchAsync_WithNewInvoices_AppliesTheWholeGraphOfEachRecord()
    {
        var withMovements = TestData.Invoice(number: 5101, payment: Payment());
        withMovements.AddCreditNote(7101, TestData.Today, 500m, TestData.Today, TestData.NowUtc);

        // Total declarado que no coincide con el detalle: se conserva fuera del sistema activo.
        var inconsistent = TestData.Invoice(
            number: 5102,
            totalAmount: 2000m,
            details: new[] { TestData.Line(unitPrice: 100m, quantity: 1) });

        var outcomes = await ImportAsync(withMovements, inconsistent);

        Assert.Equal(2, outcomes.Count);
        Assert.All(outcomes, outcome => Assert.Equal(InvoiceImportResult.Applied, outcome.Result));
        Assert.All(outcomes, outcome => Assert.NotNull(outcome.InvoiceId));

        var row = await WithReaderAsync(reader => reader.GetByNumberAsync(5101, CancellationToken.None));

        Assert.NotNull(row);
        Assert.Equal(Money.ToUnits(500m), row!.CreditNotesTotalUnits);
        Assert.Equal(Money.ToUnits(1500m), row.OutstandingBalanceUnits);
        Assert.Equal(InvoiceStatus.Partial, row.InvoiceStatus);
        Assert.Equal(PaymentStatus.Paid, row.PaymentStatus);
        Assert.True(row.HasPayment);
        Assert.Equal("Transferencia", row.PaymentMethod);

        // La factura inconsistente quedo persistida y excluida de la vista de busqueda.
        var hidden = await WithReaderAsync(reader => reader.GetByNumberAsync(5102, CancellationToken.None));

        Assert.Null(hidden);
        Assert.Equal(
            1L,
            await _host.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM invoices WHERE Number = 5102 AND IsConsistent = 0;"));
        Assert.Equal(2L, await _host.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM invoice_details;"));
    }

    [Fact]
    public async Task ImportBatchAsync_WhenTheNumberIsAlreadyPersisted_KeepsTheStoredInvoice()
    {
        var persistedId = await _host.AddInvoiceAsync(TestData.Invoice(number: 5201, totalAmount: 2000m));

        var outcomes = await ImportAsync(TestData.Invoice(number: 5201, totalAmount: 5000m));

        var outcome = Assert.Single(outcomes);

        Assert.Equal(InvoiceImportResult.AlreadyExists, outcome.Result);
        Assert.Equal(persistedId, outcome.InvoiceId);
        Assert.Equal(
            1L,
            await _host.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM invoices WHERE Number = 5201;"));
        Assert.Equal(
            Money.ToUnits(2000m),
            await _host.ExecuteScalarAsync<long>("SELECT TotalAmountUnits FROM invoices WHERE Number = 5201;"));
    }

    [Fact]
    public async Task ImportBatchAsync_WhenTheNumberRepeatsInsideTheBatch_AppliesOnlyTheFirstRecord()
    {
        var first = TestData.Invoice(
            number: 5301,
            totalAmount: 2000m,
            details: new[] { TestData.Line(unitPrice: 1000m, quantity: 2) });

        var repeated = TestData.Invoice(
            number: 5301,
            totalAmount: 3000m,
            details: new[] { TestData.Line(productName: "Duplicado", unitPrice: 1000m, quantity: 3) },
            customer: TestData.Customer(run: "99999999-9", email: "otro@demo.cl"));

        var outcomes = await ImportAsync(first, repeated);

        Assert.Equal(2, outcomes.Count);
        Assert.Equal(InvoiceImportResult.Applied, outcomes[0].Result);
        Assert.Equal(InvoiceImportResult.AlreadyExists, outcomes[1].Result);
        Assert.Equal(outcomes[0].InvoiceId, outcomes[1].InvoiceId);

        Assert.Equal(
            1L,
            await _host.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM invoices WHERE Number = 5301;"));
        Assert.Equal(
            Money.ToUnits(2000m),
            await _host.ExecuteScalarAsync<long>("SELECT TotalAmountUnits FROM invoices WHERE Number = 5301;"));
        Assert.Equal(
            1L,
            await _host.ExecuteScalarAsync<long>(
                $"SELECT COUNT(*) FROM invoice_details WHERE InvoiceId = {outcomes[0].InvoiceId};"));
    }

    [Fact]
    public async Task ImportBatchAsync_ReturnsOneOutcomePerRecordInTheSameOrder()
    {
        await _host.AddInvoiceAsync(TestData.Invoice(number: 5402));

        var outcomes = await ImportAsync(
            TestData.Invoice(number: 5401),
            TestData.Invoice(number: 5402),
            TestData.Invoice(number: 5403));

        Assert.Equal(new[] { 5401, 5402, 5403 }, outcomes.Select(outcome => outcome.Number).ToArray());
        Assert.Equal(
            new[] { InvoiceImportResult.Applied, InvoiceImportResult.AlreadyExists, InvoiceImportResult.Applied },
            outcomes.Select(outcome => outcome.Result).ToArray());
    }

    [Fact]
    public async Task ImportBatchAsync_ReplayingTheSameBatch_AppliesNothingTwice()
    {
        var invoices = new[] { TestData.Invoice(number: 5501), TestData.Invoice(number: 5502) };

        await ImportAsync(invoices);
        var replay = await ImportAsync(invoices);

        Assert.Equal(2, replay.Count);
        Assert.All(replay, outcome => Assert.Equal(InvoiceImportResult.AlreadyExists, outcome.Result));
        Assert.Equal(
            2L,
            await _host.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM invoice_details d INNER JOIN invoices i ON i.Id = d.InvoiceId " +
                "WHERE i.Number IN (5501, 5502);"));
    }

    [Fact]
    public async Task ImportBatchAsync_WhenTheCustomerRunIsAlreadyPersisted_ReusesTheSameCustomer()
    {
        var customer = TestData.Customer(run: "55555555-5", name: "Cliente Reutilizado", email: "reuso@demo.cl");

        await ImportAsync(TestData.Invoice(number: 5601, customer: customer));
        await ImportAsync(TestData.Invoice(number: 5602, customer: customer));

        Assert.Equal(
            1L,
            await _host.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM customers WHERE Run = '55555555-5';"));
        Assert.Equal(
            1L,
            await _host.ExecuteScalarAsync<long>(
                "SELECT COUNT(DISTINCT CustomerId) FROM invoices WHERE Number IN (5601, 5602);"));
    }

    [Fact]
    public async Task ImportBatchAsync_WithEmptyBatch_ReturnsNoOutcomes()
    {
        var outcomes = await WithImportAsync(port =>
            port.ImportBatchAsync(Array.Empty<InvoicePersistRecord>(), CancellationToken.None));

        Assert.Empty(outcomes);
    }
}

