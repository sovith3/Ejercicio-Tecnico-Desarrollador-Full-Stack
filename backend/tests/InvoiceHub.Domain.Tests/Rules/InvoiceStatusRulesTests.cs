using InvoiceHub.Domain.Enums;
using InvoiceHub.Domain.Rules;

namespace InvoiceHub.Domain.Tests.Rules;

/// <summary>
/// Pruebas de las reglas de calculo de estados. Estas funciones puras son la especificacion de
/// referencia de las rutinas SQL equivalentes, por lo que su comportamiento es contrato del sistema.
/// </summary>
public sealed class InvoiceStatusRulesTests
{
    [Fact]
    public void ResolveInvoiceStatus_WithoutCreditNotes_ReturnsIssued()
    {
        var status = InvoiceStatusRules.ResolveInvoiceStatus(creditNotesTotal: 0m, invoiceTotal: 2000m);

        Assert.Equal(InvoiceStatus.Issued, status);
    }

    [Fact]
    public void ResolveInvoiceStatus_WithPartialCredit_ReturnsPartial()
    {
        var status = InvoiceStatusRules.ResolveInvoiceStatus(creditNotesTotal: 1999.99m, invoiceTotal: 2000m);

        Assert.Equal(InvoiceStatus.Partial, status);
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(2500)]
    public void ResolveInvoiceStatus_WithCreditEqualToOrAboveTotal_ReturnsCancelled(decimal creditNotesTotal)
    {
        var status = InvoiceStatusRules.ResolveInvoiceStatus(creditNotesTotal, invoiceTotal: 2000m);

        Assert.Equal(InvoiceStatus.Cancelled, status);
    }

    [Fact]
    public void ResolvePaymentStatus_WithPayment_ReturnsPaidEvenWhenDueDatePassed()
    {
        var status = InvoiceStatusRules.ResolvePaymentStatus(
            hasPayment: true,
            paymentDueDate: new DateOnly(2025, 1, 1),
            today: new DateOnly(2026, 1, 15));

        Assert.Equal(PaymentStatus.Paid, status);
    }

    [Fact]
    public void ResolvePaymentStatus_DueDateBeforeToday_ReturnsOverdue()
    {
        var status = InvoiceStatusRules.ResolvePaymentStatus(
            hasPayment: false,
            paymentDueDate: new DateOnly(2026, 1, 14),
            today: new DateOnly(2026, 1, 15));

        Assert.Equal(PaymentStatus.Overdue, status);
    }

    [Fact]
    public void ResolvePaymentStatus_DueDateEqualsToday_ReturnsPending()
    {
        var status = InvoiceStatusRules.ResolvePaymentStatus(
            hasPayment: false,
            paymentDueDate: new DateOnly(2026, 1, 15),
            today: new DateOnly(2026, 1, 15));

        Assert.Equal(PaymentStatus.Pending, status);
    }

    [Fact]
    public void ResolvePaymentStatus_DueDateInTheFuture_ReturnsPending()
    {
        var status = InvoiceStatusRules.ResolvePaymentStatus(
            hasPayment: false,
            paymentDueDate: new DateOnly(2026, 2, 15),
            today: new DateOnly(2026, 1, 15));

        Assert.Equal(PaymentStatus.Pending, status);
    }

    [Fact]
    public void IsTotalConsistent_WhenAmountsMatch_ReturnsTrue()
    {
        Assert.True(InvoiceStatusRules.IsTotalConsistent(declaredTotal: 1998.5m, detailsSubtotalSum: 1998.5m));
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(-0.01)]
    [InlineData(1000)]
    public void IsTotalConsistent_WithAnyDeviation_ReturnsFalse(decimal deviation)
    {
        Assert.False(InvoiceStatusRules.IsTotalConsistent(declaredTotal: 2000m, detailsSubtotalSum: 2000m + deviation));
    }

    [Fact]
    public void DescribeInconsistency_WhenConsistent_ReturnsNull()
    {
        Assert.Null(InvoiceStatusRules.DescribeInconsistency(2000m, 2000m));
    }

    [Fact]
    public void DescribeInconsistency_WhenDivergent_DescribesBothAmounts()
    {
        var reason = InvoiceStatusRules.DescribeInconsistency(declaredTotal: 2000m, detailsSubtotalSum: 1900m);

        Assert.NotNull(reason);
        // Se compara contra el mismo formateo ("N2") para que la asercion no dependa de la cultura.
        Assert.Contains(2000m.ToString("N2"), reason);
        Assert.Contains(1900m.ToString("N2"), reason);
    }
}
