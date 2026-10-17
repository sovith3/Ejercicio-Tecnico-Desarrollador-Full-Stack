using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceHub.Infrastructure.Tests.Persistence;

/// <summary>
/// Pruebas del lector de busqueda sobre la vista <c>vw_invoice_search</c>. Verifican que el filtrado, el
/// ordenamiento (incluidos los identificadores de columna en camelCase que compara el SQL) y la paginacion
/// se resuelvan en el motor, y que las facturas inconsistentes queden fuera del sistema activo.
/// </summary>
public sealed class InvoiceSearchReaderTests : IAsyncLifetime
{
    private static readonly DateOnly RealToday = TestData.BusinessToday();

    private readonly TestHost _host = new();

    public async Task InitializeAsync() => await _host.InitializeAsync();

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    private Task<T> WithReaderAsync<T>(Func<IInvoiceSearchReader, Task<T>> action)
        => _host.WithScopeAsync(provider => action(provider.GetRequiredService<IInvoiceSearchReader>()));

    private Task<T> WithStatusesAsync<T>(Func<IStatusRecalculationPort, Task<T>> action)
        => _host.WithScopeAsync(provider => action(provider.GetRequiredService<IStatusRecalculationPort>()));

    private Task<T> WithCreditNotesAsync<T>(Func<ICreditNoteWritePort, Task<T>> action)
        => _host.WithScopeAsync(provider => action(provider.GetRequiredService<ICreditNoteWritePort>()));

    private Task<T> WithPaymentsAsync<T>(Func<IPaymentWritePort, Task<T>> action)
        => _host.WithScopeAsync(provider => action(provider.GetRequiredService<IPaymentWritePort>()));

    private async Task<long> AddAsync(
        int number,
        decimal total = 2000m,
        int dueInDays = 20,
        string? customerName = null)
    {
        var invoice = TestData.Invoice(
            number: number,
            totalAmount: total,
            details: new[] { TestData.Line(unitPrice: total, quantity: 1) },
            invoiceDate: RealToday.AddDays(-30),
            paymentDueDate: RealToday.AddDays(dueInDays),
            daysToDue: 30,
            customer: TestData.Customer(
                run: $"RUN-{number}",
                name: customerName ?? $"Cliente {number}",
                email: $"cliente{number}@demo.cl"));

        var invoiceId = await _host.AddInvoiceAsync(invoice);

        // El estado de cobro se recalcula con la fecha de negocio del reloj real para que las fechas de las
        // pruebas (relativas a hoy) queden evaluadas correctamente en el motor.
        await RecalculateAsync(invoiceId);

        return invoiceId;
    }

    private Task RecalculateAsync(long invoiceId)
        => WithStatusesAsync(statuses =>
            statuses.RecalculateInvoiceStatusesAsync(invoiceId, CancellationToken.None));

    private static InvoiceSearchCriteria Criteria(
        int page = 1,
        int pageSize = 25,
        string sortBy = InvoiceSearchCriteriaNormalizer.DefaultSortBy,
        string sortDirection = "desc")
        => new()
        {
            Page = page,
            PageSize = pageSize,
            SortBy = sortBy,
            SortDirection = sortDirection
        };

    [Fact]
    public async Task SearchAsync_WithoutFilters_ExcludesInconsistentInvoices()
    {
        await AddAsync(number: 8001);
        await _host.AddInvoiceAsync(TestData.Invoice(
            number: 8002,
            totalAmount: 2000m,
            details: new[] { TestData.Line(unitPrice: 100m, quantity: 1) }));

        var page = await WithReaderAsync(reader => reader.SearchAsync(Criteria(), CancellationToken.None));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(8001, Assert.Single(page.Items).Number);
    }

    [Fact]
    public async Task SearchAsync_WithNumberFilter_ReturnsOnlyThatInvoice()
    {
        await AddAsync(number: 8003);
        await AddAsync(number: 8004);

        var criteria = Criteria();
        criteria.Number = 8004;

        var page = await WithReaderAsync(reader => reader.SearchAsync(criteria, CancellationToken.None));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(8004, Assert.Single(page.Items).Number);
    }

    [Fact]
    public async Task SearchAsync_WithCustomerRunAndText_FiltersByPartialMatch()
    {
        await AddAsync(number: 8005, customerName: "ACME Servicios SpA");
        await AddAsync(number: 8006, customerName: "Beta Comercial Ltda");

        var criteria = Criteria();
        criteria.CustomerRun = "run-8005";
        criteria.Text = "acme";

        var page = await WithReaderAsync(reader => reader.SearchAsync(criteria, CancellationToken.None));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(8005, Assert.Single(page.Items).Number);
    }

    [Fact]
    public async Task SearchAsync_WithInvoiceStatusFilter_ReturnsMatchingState()
    {
        var partialId = await AddAsync(number: 8007);
        await AddAsync(number: 8008);

        await WithCreditNotesAsync(creditNotes => creditNotes.AppendCreditNoteAsync(
            new CreditNoteAppendRequest(partialId, 9301, RealToday, Money.ToUnits(500m), TestData.NowUtc),
            CancellationToken.None));
        await RecalculateAsync(partialId);

        var criteria = Criteria();
        criteria.InvoiceStatus = InvoiceStatus.Partial;

        var page = await WithReaderAsync(reader => reader.SearchAsync(criteria, CancellationToken.None));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(8007, Assert.Single(page.Items).Number);
        Assert.Equal(Money.ToUnits(1500m), page.Items[0].OutstandingBalanceUnits);
    }

    [Fact]
    public async Task SearchAsync_WithPaymentStatusFilter_ReturnsOverdueInvoices()
    {
        await AddAsync(number: 8009, dueInDays: -5);
        await AddAsync(number: 8010, dueInDays: 5);

        var criteria = Criteria();
        criteria.PaymentStatus = PaymentStatus.Overdue;

        var page = await WithReaderAsync(reader => reader.SearchAsync(criteria, CancellationToken.None));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(8009, Assert.Single(page.Items).Number);
    }

    [Fact]
    public async Task SearchAsync_FilteringByInconsistency_ReturnsTheExcludedUniverse()
    {
        await AddAsync(number: 8101);
        await _host.AddInvoiceAsync(TestData.Invoice(
            number: 8102,
            totalAmount: 2000m,
            details: new[] { TestData.Line(unitPrice: 100m, quantity: 1) }));

        var onlyInconsistent = Criteria();
        onlyInconsistent.IsConsistent = false;
        onlyInconsistent.OnlyActiveSystem = false;

        var withEverything = Criteria();
        withEverything.OnlyActiveSystem = false;

        var inconsistentPage = await WithReaderAsync(reader =>
            reader.SearchAsync(onlyInconsistent, CancellationToken.None));
        var allPage = await WithReaderAsync(reader => reader.SearchAsync(withEverything, CancellationToken.None));

        Assert.Equal(1, inconsistentPage.TotalCount);
        Assert.Equal(8102, Assert.Single(inconsistentPage.Items).Number);
        Assert.Equal(2, allPage.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_SortByDueDateDescending_UsesTheSqlContractFieldName()
    {
        await AddAsync(number: 8201, dueInDays: -20);
        await AddAsync(number: 8202, dueInDays: 15);
        await AddAsync(number: 8203, dueInDays: 5);

        var criteria = Criteria(sortBy: "dueDate", sortDirection: "desc");

        var page = await WithReaderAsync(reader => reader.SearchAsync(criteria, CancellationToken.None));

        Assert.Equal(new[] { 8202, 8203, 8201 }, page.Items.Select(item => item.Number));
    }

    [Fact]
    public async Task SearchAsync_SortByDueDateAscending_UsesTheSqlContractFieldName()
    {
        await AddAsync(number: 8204, dueInDays: -20);
        await AddAsync(number: 8205, dueInDays: 15);
        await AddAsync(number: 8206, dueInDays: 5);

        var criteria = Criteria(sortBy: "dueDate", sortDirection: "asc");

        var page = await WithReaderAsync(reader => reader.SearchAsync(criteria, CancellationToken.None));

        Assert.Equal(new[] { 8204, 8206, 8205 }, page.Items.Select(item => item.Number));
    }

    [Fact]
    public async Task SearchAsync_SortByCustomerAscending_OrdersByName()
    {
        await AddAsync(number: 8207, customerName: "Zeta SpA");
        await AddAsync(number: 8208, customerName: "Alfa Ltda");
        await AddAsync(number: 8209, customerName: "Media SA");

        var criteria = Criteria(sortBy: "customer", sortDirection: "asc");

        var page = await WithReaderAsync(reader => reader.SearchAsync(criteria, CancellationToken.None));

        Assert.Equal(new[] { 8208, 8209, 8207 }, page.Items.Select(item => item.Number));
    }

    [Fact]
    public async Task SearchAsync_SortByOutstandingAscending_OrdersByRemainingBalance()
    {
        await AddAsync(number: 8301, total: 3000m);
        var reducedId = await AddAsync(number: 8302, total: 3000m);
        await AddAsync(number: 8303, total: 3000m);

        await WithCreditNotesAsync(creditNotes => creditNotes.AppendCreditNoteAsync(
            new CreditNoteAppendRequest(reducedId, 9401, RealToday, Money.ToUnits(2500m), TestData.NowUtc),
            CancellationToken.None));
        await RecalculateAsync(reducedId);

        var criteria = Criteria(sortBy: "outstanding", sortDirection: "asc");

        var page = await WithReaderAsync(reader => reader.SearchAsync(criteria, CancellationToken.None));

        Assert.Equal(8302, page.Items[0].Number);
        Assert.Equal(Money.ToUnits(500m), page.Items[0].OutstandingBalanceUnits);
    }

    [Fact]
    public async Task SearchAsync_SortByTotalDescending_OrdersByBilledAmount()
    {
        await AddAsync(number: 8401, total: 1000m);
        await AddAsync(number: 8402, total: 9000m);
        await AddAsync(number: 8403, total: 5000m);

        var criteria = Criteria(sortBy: "total", sortDirection: "desc");

        var page = await WithReaderAsync(reader => reader.SearchAsync(criteria, CancellationToken.None));

        Assert.Equal(new[] { 8402, 8403, 8401 }, page.Items.Select(item => item.Number));
    }

    [Fact]
    public async Task SearchAsync_WithPaging_ReturnsTheRequestedWindowAndTheTotalCount()
    {
        for (var number = 8501; number <= 8507; number++)
        {
            await AddAsync(number: number);
        }

        var firstPage = await WithReaderAsync(reader =>
            reader.SearchAsync(Criteria(page: 1, pageSize: 3), CancellationToken.None));
        var lastPage = await WithReaderAsync(reader =>
            reader.SearchAsync(Criteria(page: 3, pageSize: 3), CancellationToken.None));

        Assert.Equal(7, firstPage.TotalCount);
        Assert.Equal(3, firstPage.Items.Count);
        Assert.Single(lastPage.Items);
        Assert.Equal(3, lastPage.TotalPages);
    }

    [Fact]
    public async Task SearchAsync_WithDueSoonWindow_ReturnsOverdueAndUpcomingInvoices()
    {
        await AddAsync(number: 8601, dueInDays: -40);
        await AddAsync(number: 8602, dueInDays: -5);
        await AddAsync(number: 8603, dueInDays: 10);
        await AddAsync(number: 8604, dueInDays: 60);

        // El criterio documenta "vencimiento superado o por vencer dentro de N dias": incluye la mora y la
        // cartera proxima a vencer, y excluye lo que queda fuera de la ventana.
        var criteria = Criteria();
        criteria.MinDaysOverdue = 30;

        var page = await WithReaderAsync(reader => reader.SearchAsync(criteria, CancellationToken.None));

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(new[] { 8601, 8602, 8603 }, page.Items.Select(item => item.Number).OrderBy(number => number));
        Assert.True(page.Items.Single(item => item.Number == 8603).DaysOverdue == 0);
        Assert.True(page.Items.Single(item => item.Number == 8601).DaysOverdue >= 40);
    }

    [Fact]
    public async Task GetByNumberAsync_ForUnknownNumber_ReturnsNull()
    {
        var row = await WithReaderAsync(reader => reader.GetByNumberAsync(999_999, CancellationToken.None));

        Assert.Null(row);
    }

    [Fact]
    public async Task GetByNumberAsync_ExposesPaymentAndCustomerData()
    {
        var invoiceId = await AddAsync(number: 8701, total: 4000m, dueInDays: -10, customerName: "Customer Data SpA");

        await WithPaymentsAsync(payments => payments.RegisterPaymentAsync(
            new PaymentAppendRequest(invoiceId, "Cheque", RealToday, TestData.NowUtc),
            CancellationToken.None));
        await RecalculateAsync(invoiceId);

        var row = await WithReaderAsync(reader => reader.GetByNumberAsync(8701, CancellationToken.None));

        Assert.NotNull(row);
        Assert.Equal("Customer Data SpA", row!.CustomerName);
        Assert.Equal("RUN-8701", row.CustomerRun);
        Assert.Equal("cliente8701@demo.cl", row.CustomerEmail);
        Assert.True(row.HasPayment);
        Assert.Equal("Cheque", row.PaymentMethod);
        Assert.Equal(RealToday, row.PaymentDate);
        Assert.Equal(PaymentStatus.Paid, row.PaymentStatus);
        Assert.Equal(Money.ToUnits(4000m), row.TotalAmountUnits);
        Assert.Equal(Money.ToUnits(4000m), row.OutstandingBalanceUnits);
    }

    [Fact]
    public async Task CountByInvoiceStatusAsync_AggregatesCountAndAmountPerStatus()
    {
        var partialId = await AddAsync(number: 8801, total: 2000m);
        await AddAsync(number: 8802, total: 3000m);
        await WithCreditNotesAsync(creditNotes => creditNotes.AppendCreditNoteAsync(
            new CreditNoteAppendRequest(partialId, 9501, RealToday, Money.ToUnits(500m), TestData.NowUtc),
            CancellationToken.None));
        await RecalculateAsync(partialId);

        var counts = await WithReaderAsync(reader =>
            reader.CountByInvoiceStatusAsync(onlyConsistent: true, CancellationToken.None));

        var issued = counts.Single(row => row.InvoiceStatus == InvoiceStatus.Issued);
        var partial = counts.Single(row => row.InvoiceStatus == InvoiceStatus.Partial);

        Assert.Equal(1, issued.InvoiceCount);
        Assert.Equal(Money.ToUnits(3000m), issued.TotalAmountUnits);
        Assert.Equal(1, partial.InvoiceCount);
        Assert.Equal(Money.ToUnits(2000m), partial.TotalAmountUnits);
    }

    [Fact]
    public async Task CountByPaymentStatusAsync_WithActiveSystemOnly_ExcludesInconsistentInvoices()
    {
        await AddAsync(number: 8901, dueInDays: -10);
        await _host.AddInvoiceAsync(TestData.Invoice(
            number: 8902,
            totalAmount: 2000m,
            details: new[] { TestData.Line(unitPrice: 100m, quantity: 1) }));

        var activeOnly = await WithReaderAsync(reader =>
            reader.CountByPaymentStatusAsync(onlyConsistent: true, CancellationToken.None));
        var everyInvoice = await WithReaderAsync(reader =>
            reader.CountByPaymentStatusAsync(onlyConsistent: false, CancellationToken.None));

        Assert.Equal(1, activeOnly.Where(row => row.PaymentStatus == PaymentStatus.Overdue).Sum(row => row.InvoiceCount));
        Assert.Equal(2, everyInvoice.Sum(row => row.InvoiceCount));
    }
}
