using InvoiceHub.Application.Common;
using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceHub.Infrastructure.Tests.Persistence;

/// <summary>
/// Pruebas de los reportes y del panel sobre las consultas SQL del repositorio: resumen por estado de cobro
/// (reporte 2), facturas vencidas sin pago ni nota de credito (reporte 1), facturas inconsistentes
/// (reporte 3) e instantanea consolidada del panel principal.
/// </summary>
public sealed class InvoiceReportReaderTests : IAsyncLifetime
{
    private static readonly DateOnly RealToday = TestData.BusinessToday();

    private readonly TestHost _host = new();

    public async Task InitializeAsync() => await _host.InitializeAsync();

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    private Task<T> WithReportsAsync<T>(Func<IInvoiceReportReader, Task<T>> action)
        => _host.WithScopeAsync(provider => action(provider.GetRequiredService<IInvoiceReportReader>()));

    private Task<T> WithStatusesAsync<T>(Func<IStatusRecalculationPort, Task<T>> action)
        => _host.WithScopeAsync(provider => action(provider.GetRequiredService<IStatusRecalculationPort>()));

    private Task<T> WithCreditNotesAsync<T>(Func<ICreditNoteWritePort, Task<T>> action)
        => _host.WithScopeAsync(provider => action(provider.GetRequiredService<ICreditNoteWritePort>()));

    private Task<T> WithPaymentsAsync<T>(Func<IPaymentWritePort, Task<T>> action)
        => _host.WithScopeAsync(provider => action(provider.GetRequiredService<IPaymentWritePort>()));

    private async Task<long> AddAsync(
        int number,
        decimal total,
        int dueInDays,
        DateOnly? invoiceDate = null,
        string? customerName = null)
    {
        var invoice = TestData.Invoice(
            number: number,
            totalAmount: total,
            details: new[] { TestData.Line(unitPrice: total, quantity: 1) },
            invoiceDate: invoiceDate ?? RealToday,
            paymentDueDate: RealToday.AddDays(dueInDays),
            daysToDue: 30,
            customer: TestData.Customer(
                run: $"RUN-{number}",
                name: customerName ?? $"Cliente {number}",
                email: $"cliente{number}@demo.cl"));

        var invoiceId = await _host.AddInvoiceAsync(invoice);

        await WithStatusesAsync(statuses =>
            statuses.RecalculateInvoiceStatusesAsync(invoiceId, CancellationToken.None));

        return invoiceId;
    }

    private async Task AppendCreditNoteAsync(long invoiceId, int number, decimal amount)
    {
        await WithCreditNotesAsync(creditNotes => creditNotes.AppendCreditNoteAsync(
            new CreditNoteAppendRequest(invoiceId, number, RealToday, Money.ToUnits(amount), TestData.NowUtc),
            CancellationToken.None));

        await WithStatusesAsync(statuses =>
            statuses.RecalculateInvoiceStatusesAsync(invoiceId, CancellationToken.None));
    }

    private async Task RegisterPaymentAsync(long invoiceId, string method = "Transferencia")
    {
        await WithPaymentsAsync(payments => payments.RegisterPaymentAsync(
            new PaymentAppendRequest(invoiceId, method, RealToday, TestData.NowUtc),
            CancellationToken.None));

        await WithStatusesAsync(statuses =>
            statuses.RecalculateInvoiceStatusesAsync(invoiceId, CancellationToken.None));
    }

    [Fact]
    public async Task GetPaymentStatusSummaryAsync_AggregatesEveryStatusWithPercentages()
    {
        await AddAsync(number: 9001, total: 2000m, dueInDays: -10);
        await AddAsync(number: 9002, total: 3000m, dueInDays: -5);
        await AddAsync(number: 9003, total: 1000m, dueInDays: 10);
        var paidId = await AddAsync(number: 9004, total: 4000m, dueInDays: 10);
        await RegisterPaymentAsync(paidId);

        var summary = await WithReportsAsync(reader =>
            reader.GetPaymentStatusSummaryAsync(CancellationToken.None));

        Assert.Equal(3, summary.Count);

        var overdue = summary.Single(row => row.PaymentStatus == PaymentStatus.Overdue);
        var pending = summary.Single(row => row.PaymentStatus == PaymentStatus.Pending);
        var paid = summary.Single(row => row.PaymentStatus == PaymentStatus.Paid);

        Assert.Equal(2, overdue.InvoiceCount);
        Assert.Equal(Money.ToUnits(5000m), overdue.TotalAmountUnits);
        Assert.Equal(1, pending.InvoiceCount);
        Assert.Equal(Money.ToUnits(1000m), pending.TotalAmountUnits);
        Assert.Equal(1, paid.InvoiceCount);
        Assert.Equal(Money.ToUnits(4000m), paid.TotalAmountUnits);

        Assert.Equal(100d, summary.Sum(row => row.AmountPercentage), 3);
        Assert.Equal(100d, summary.Sum(row => row.CountPercentage), 3);
        Assert.Equal(50d, overdue.AmountPercentage, 3);
    }

    [Fact]
    public async Task GetPaymentStatusSummaryAsync_IgnoresInconsistentInvoices()
    {
        await AddAsync(number: 9101, total: 2000m, dueInDays: -10);
        await _host.AddInvoiceAsync(TestData.Invoice(
            number: 9102,
            totalAmount: 5000m,
            details: new[] { TestData.Line(unitPrice: 100m, quantity: 1) }));

        var summary = await WithReportsAsync(reader =>
            reader.GetPaymentStatusSummaryAsync(CancellationToken.None));

        Assert.Equal(1, summary.Sum(row => row.InvoiceCount));
        Assert.Equal(Money.ToUnits(2000m), summary.Sum(row => row.TotalAmountUnits));
    }

    [Fact]
    public async Task GetOverdueWithoutActivityAsync_ReturnsOnlyOverdueInvoicesWithoutPaymentNorCreditNote()
    {
        await AddAsync(number: 9201, total: 2000m, dueInDays: -60);
        var paidId = await AddAsync(number: 9202, total: 2000m, dueInDays: -60);
        var creditedId = await AddAsync(number: 9203, total: 2000m, dueInDays: -60);
        await AddAsync(number: 9204, total: 2000m, dueInDays: -5);

        await RegisterPaymentAsync(paidId);
        await AppendCreditNoteAsync(creditedId, 9601, 500m);

        var overdue = await WithReportsAsync(reader =>
            reader.GetOverdueWithoutActivityAsync(30, CancellationToken.None));

        var row = Assert.Single(overdue);
        Assert.Equal(9201, row.Number);
        Assert.True(row.DaysOverdue >= 60);
        Assert.Equal(Money.ToUnits(2000m), row.TotalAmountUnits);
        Assert.Equal("RUN-9201", row.CustomerRun);
        Assert.Equal("Cliente 9201", row.CustomerName);
        Assert.Equal("cliente9201@demo.cl", row.CustomerEmail);
        Assert.Equal(InvoiceStatus.Issued, row.InvoiceStatus);
    }

    [Fact]
    public async Task GetOverdueWithoutActivityAsync_WithHighThreshold_ReturnsNothing()
    {
        await AddAsync(number: 9301, total: 2000m, dueInDays: -60);

        var overdue = await WithReportsAsync(reader =>
            reader.GetOverdueWithoutActivityAsync(365, CancellationToken.None));

        Assert.Empty(overdue);
    }

    [Fact]
    public async Task GetInconsistentInvoicesAsync_ReportsTheDeclaredVersusDetailDifference()
    {
        await AddAsync(number: 9401, total: 2000m, dueInDays: 10);
        await _host.AddInvoiceAsync(TestData.Invoice(
            number: 9402,
            totalAmount: 2000m,
            details: new[] { TestData.Line(unitPrice: 900m, quantity: 2) },
            customer: TestData.Customer(run: "RUN-9402", name: "Cliente 9402", email: "cliente9402@demo.cl")));

        var inconsistent = await WithReportsAsync(reader =>
            reader.GetInconsistentInvoicesAsync(CancellationToken.None));

        var row = Assert.Single(inconsistent);
        Assert.Equal(9402, row.Number);
        Assert.Equal(Money.ToUnits(2000m), row.DeclaredTotalUnits);
        Assert.Equal(Money.ToUnits(1800m), row.DetailsSubtotalUnits);
        // La diferencia se define como total declarado menos suma del detalle.
        Assert.Equal(Money.ToUnits(200m), row.DifferenceUnits);
        Assert.Equal(1, row.DetailLineCount);
        Assert.NotNull(row.InconsistencyReason);
        Assert.Equal("RUN-9402", row.CustomerRun);
    }

    [Fact]
    public async Task GetDashboardAsync_ConsolidatesEveryUniverseOfTheDashboard()
    {
        await AddAsync(number: 9501, total: 2000m, dueInDays: -10);
        await AddAsync(number: 9502, total: 4000m, dueInDays: 3);
        var partialId = await AddAsync(number: 9503, total: 3000m, dueInDays: 20);
        var paidId = await AddAsync(number: 9504, total: 5000m, dueInDays: 20);
        await AppendCreditNoteAsync(partialId, 9701, 1000m);
        await RegisterPaymentAsync(paidId);
        await _host.AddInvoiceAsync(TestData.Invoice(
            number: 9505,
            totalAmount: 2000m,
            details: new[] { TestData.Line(unitPrice: 1800m, quantity: 1) }));

        var snapshot = await WithReportsAsync(reader => reader.GetDashboardAsync(CancellationToken.None));

        Assert.Equal(5, snapshot.TotalInvoices);
        Assert.Equal(4, snapshot.ActiveInvoices);
        Assert.Equal(1, snapshot.InconsistentInvoices);
        Assert.Equal(Money.ToUnits(16_000m), snapshot.TotalBilledUnits);
        Assert.Equal(Money.ToUnits(14_000m), snapshot.ActiveBilledUnits);
        Assert.Equal(Money.ToUnits(13_000m), snapshot.OutstandingUnits);
        Assert.Equal(Money.ToUnits(1000m), snapshot.CreditNotesUnits);
        Assert.Equal(1, snapshot.OverdueCount);
        Assert.Equal(Money.ToUnits(2000m), snapshot.OverdueAmountUnits);
        Assert.Equal(1, snapshot.DueSoonCount);
        Assert.Equal(Money.ToUnits(4000m), snapshot.DueSoonAmountUnits);
    }

    [Fact]
    public async Task GetDashboardAsync_BreaksDownByStatusMonthAndDebtor()
    {
        await AddAsync(number: 9601, total: 2000m, dueInDays: -10);
        var partialId = await AddAsync(number: 9602, total: 3000m, dueInDays: 20);
        await AppendCreditNoteAsync(partialId, 9801, 1000m);

        var snapshot = await WithReportsAsync(reader => reader.GetDashboardAsync(CancellationToken.None));

        var byInvoiceStatus = snapshot.ByInvoiceStatus.Sum(row => row.InvoiceCount);
        var issued = snapshot.ByInvoiceStatus.Single(row => row.InvoiceStatus == InvoiceStatus.Issued);

        Assert.Equal(snapshot.ActiveInvoices, byInvoiceStatus);
        Assert.Equal(1, issued.InvoiceCount);
        Assert.Equal(Money.ToUnits(2000m), issued.TotalAmountUnits);
        Assert.Equal(snapshot.ActiveInvoices, snapshot.ByPaymentStatus.Sum(row => row.InvoiceCount));

        var month = Assert.Single(snapshot.MonthlyBilling);
        Assert.Equal(RealToday.ToString("yyyy-MM"), month.Period);
        Assert.Equal(2, month.InvoiceCount);
        Assert.Equal(Money.ToUnits(5000m), month.TotalAmountUnits);
        Assert.Equal(Money.ToUnits(1000m), month.CreditNotesUnits);

        var debtor = Assert.Single(snapshot.TopDebtors);
        Assert.Equal("RUN-9601", debtor.CustomerRun);
        Assert.Equal(1, debtor.OverdueCount);
        Assert.Equal(Money.ToUnits(2000m), debtor.OutstandingUnits);
    }

    [Fact]
    public async Task GetDashboardAsync_WithAnEmptyDatabase_ReturnsZeroedSnapshot()
    {
        var snapshot = await WithReportsAsync(reader => reader.GetDashboardAsync(CancellationToken.None));

        Assert.Equal(0, snapshot.TotalInvoices);
        Assert.Equal(0, snapshot.ActiveInvoices);
        Assert.Equal(0L, snapshot.TotalBilledUnits);
        Assert.Equal(0L, snapshot.CreditNotesUnits);
        Assert.Empty(snapshot.ByInvoiceStatus);
        Assert.Empty(snapshot.ByPaymentStatus);
        Assert.Empty(snapshot.MonthlyBilling);
        Assert.Empty(snapshot.TopDebtors);
    }
}
