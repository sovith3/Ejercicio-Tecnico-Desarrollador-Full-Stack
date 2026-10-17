using InvoiceHub.Application.Common;
using InvoiceHub.Application.Tests.Fakes;
using InvoiceHub.Application.UseCases;
using InvoiceHub.Domain.Entities;
using InvoiceHub.Domain.Enums;

namespace InvoiceHub.Application.Tests.UseCases;

/// <summary>
/// Pruebas del caso de uso de detalle de factura. El detalle debe exponer siempre el estado recalculado
/// con la fecha de negocio vigente, ademas del estado declarado en el origen de datos.
/// </summary>
public sealed class GetInvoiceDetailHandlerTests
{
    private readonly FakeInvoiceRepository _invoices = new();
    private readonly InMemoryCacheStore _cache = new();
    private readonly FixedClock _clock = FixedClock.Default();

    private GetInvoiceDetailHandler CreateHandler() => new(_invoices, _cache, _clock);

    [Fact]
    public async Task HandleAsync_WhenInvoiceExists_ReturnsDetailWithRecalculatedStatus()
    {
        _invoices.Invoices.Add(TestRows.Invoice(
            number: 1000,
            paymentDueDate: TestRows.Today.AddDays(-10),
            declaredPaymentStatus: PaymentStatus.Pending,
            declaredStatus: InvoiceStatus.Partial));

        var detail = await CreateHandler().HandleAsync(1000, CancellationToken.None);

        Assert.Equal(1000, detail.Number);
        Assert.Equal(InvoiceStatus.Issued, detail.InvoiceStatus);
        Assert.Equal(PaymentStatus.Overdue, detail.PaymentStatus);
        Assert.Equal(10, detail.DaysOverdue);
        Assert.Equal(InvoiceStatus.Partial, detail.DeclaredStatus);
        Assert.Equal(PaymentStatus.Pending, detail.DeclaredPaymentStatus);
        Assert.True(detail.IsDeclaredStatusDivergent);
        Assert.True(detail.IsDeclaredPaymentStatusDivergent);
        Assert.Equal(2000m, detail.TotalAmount);
        Assert.Equal(2000m, detail.DetailsSubtotalSum);
        Assert.Equal(2000m, detail.OutstandingBalance);
        Assert.True(detail.IsConsistent);
        Assert.Single(detail.Details);
        Assert.Empty(detail.CreditNotes);
        Assert.Null(detail.Payment);
    }

    [Fact]
    public async Task HandleAsync_WhenInvoiceDoesNotExist_ThrowsNotFoundWithoutCaching()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateHandler().HandleAsync(4242, CancellationToken.None));

        Assert.Equal(0, _cache.SetCount);
    }

    [Fact]
    public async Task HandleAsync_SecondCall_IsServedFromCache()
    {
        _invoices.Invoices.Add(TestRows.Invoice());

        var handler = CreateHandler();

        await handler.HandleAsync(1000, CancellationToken.None);
        await handler.HandleAsync(1000, CancellationToken.None);

        Assert.Equal(1, _invoices.GetByNumberCallCount);
        Assert.True(_cache.Contains(CacheKeys.InvoiceDetail(1000)));
    }

    [Fact]
    public async Task HandleAsync_UsesIndependentCacheEntryPerInvoice()
    {
        _invoices.Invoices.Add(TestRows.Invoice(number: 1000));
        _invoices.Invoices.Add(TestRows.Invoice(number: 2000));

        var handler = CreateHandler();

        await handler.HandleAsync(1000, CancellationToken.None);
        await handler.HandleAsync(2000, CancellationToken.None);

        Assert.Equal(2, _invoices.GetByNumberCallCount);
        Assert.True(_cache.Contains(CacheKeys.InvoiceDetail(1000)));
        Assert.True(_cache.Contains(CacheKeys.InvoiceDetail(2000)));
    }

    [Fact]
    public async Task HandleAsync_InvalidatedCacheForcesFreshRead()
    {
        _invoices.Invoices.Add(TestRows.Invoice());
        var handler = CreateHandler();

        await handler.HandleAsync(1000, CancellationToken.None);
        await _cache.InvalidateInvoiceReadModelsAsync(CancellationToken.None);
        await handler.HandleAsync(1000, CancellationToken.None);

        Assert.Equal(2, _invoices.GetByNumberCallCount);
    }

    [Fact]
    public async Task HandleAsync_WithCreditNotes_OrdersThemByDateAndNumber()
    {
        var invoice = TestRows.Invoice(totalAmount: 2000m);
        invoice.AddCreditNote(7002, TestRows.Today, 100m, TestRows.Today, TestRows.NowUtc);
        invoice.AddCreditNote(7001, TestRows.Today.AddDays(-3), 200m, TestRows.Today, TestRows.NowUtc);
        _invoices.Invoices.Add(invoice);

        var detail = await CreateHandler().HandleAsync(1000, CancellationToken.None);

        Assert.Equal(InvoiceStatus.Partial, detail.InvoiceStatus);
        Assert.Equal(300m, detail.CreditNotesTotal);
        Assert.Equal(1700m, detail.OutstandingBalance);
        Assert.Equal(new[] { 7001, 7002 }, detail.CreditNotes.Select(note => note.Number));
    }

    [Fact]
    public async Task HandleAsync_WithPayment_ReturnsPaidDetail()
    {
        _invoices.Invoices.Add(TestRows.Invoice(
            payment: InvoicePayment.Create("Transferencia", TestRows.Today.AddDays(-2), TestRows.NowUtc)));

        var detail = await CreateHandler().HandleAsync(1000, CancellationToken.None);

        Assert.Equal(PaymentStatus.Paid, detail.PaymentStatus);
        Assert.NotNull(detail.Payment);
        Assert.Equal("Transferencia", detail.Payment!.PaymentMethod);
        Assert.Equal(TestRows.Today.AddDays(-2), detail.Payment.PaymentDate);
    }
}
