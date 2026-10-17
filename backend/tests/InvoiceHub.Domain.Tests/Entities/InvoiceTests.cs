using InvoiceHub.Domain.Common;
using InvoiceHub.Domain.Entities;
using InvoiceHub.Domain.Enums;
using InvoiceHub.Domain.Exceptions;
using InvoiceHub.Domain.Tests.Builders;

namespace InvoiceHub.Domain.Tests.Entities;

/// <summary>
/// Pruebas de la raiz de agregado <see cref="Invoice"/>: invariantes del constructor, consistencia entre
/// el total declarado y el detalle, emision de notas de credito y registro de pago.
/// </summary>
public sealed class InvoiceTests
{
    [Fact]
    public void Constructor_WithoutDetailLines_ThrowsInvariantViolation()
    {
        var exception = Assert.Throws<InvariantViolationException>(() =>
            InvoiceTestData.Invoice(details: Array.Empty<InvoiceDetail>()));

        Assert.Contains("1000", exception.Message);
    }

    [Fact]
    public void Constructor_WithNegativeTotal_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InvoiceTestData.Invoice(totalAmount: -1m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Constructor_WithNonPositiveNumber_Throws(int number)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InvoiceTestData.Invoice(number: number));
    }

    [Fact]
    public void Constructor_WhenDetailSumMatchesTotal_IsConsistent()
    {
        var invoice = InvoiceTestData.Invoice(totalAmount: 2500m, details: new[]
        {
            InvoiceTestData.Detail(unitPrice: 1000m, quantity: 2),
            InvoiceTestData.Detail(unitPrice: 500m, quantity: 1)
        });

        Assert.True(invoice.IsConsistent);
        Assert.Null(invoice.InconsistencyReason);
        Assert.Equal(2500m, invoice.DetailsSubtotalSum);
        Assert.False(invoice.HasInconsistentDetailLines);
    }

    [Fact]
    public void Constructor_WhenDetailSumDiffersFromTotal_FlagsInconsistencyWithReason()
    {
        var invoice = InvoiceTestData.Invoice(totalAmount: 2000m, details: new[]
        {
            InvoiceTestData.Detail(unitPrice: 900m, quantity: 2)
        });

        Assert.False(invoice.IsConsistent);
        Assert.NotNull(invoice.InconsistencyReason);
        Assert.Contains(MoneyFormat.Amount(2000m), invoice.InconsistencyReason);
    }

    [Fact]
    public void Constructor_WhenLineSubtotalDoesNotMatchUnitPriceTimesQuantity_DetectsInconsistentLine()
    {
        var invoice = InvoiceTestData.Invoice(totalAmount: 2000m, details: new[]
        {
            // El subtotal informado (2.000) cuadra con el total, pero no con 900 x 2.
            InvoiceTestData.Detail(unitPrice: 900m, quantity: 2, subtotal: 2000m)
        });

        Assert.True(invoice.IsConsistent);
        Assert.True(invoice.HasInconsistentDetailLines);
        Assert.Equal(1800m, invoice.Details.First().ExpectedSubtotal);
    }

    [Fact]
    public void Constructor_WithDeclaredStatuses_ExposesDivergenceFlags()
    {
        var invoice = InvoiceTestData.Invoice(
            paymentDueDate: InvoiceTestData.Today.AddDays(-5),
            declaredStatus: InvoiceStatus.Partial,
            declaredPaymentStatus: PaymentStatus.Pending);

        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.Equal(PaymentStatus.Overdue, invoice.PaymentStatus);
        Assert.True(invoice.IsDeclaredStatusDivergent);
        Assert.True(invoice.IsDeclaredPaymentStatusDivergent);
    }

    [Fact]
    public void AddCreditNote_WithPartialAmount_UpdatesStatusBalanceAndTimestamp()
    {
        var invoice = InvoiceTestData.Invoice(totalAmount: 2000m);
        var creditNoteDate = InvoiceTestData.Today.AddDays(-1);
        var createdUtc = InvoiceTestData.NowUtc.AddMinutes(5);

        var creditNote = invoice.AddCreditNote(
            number: 7001,
            creditNoteDate: creditNoteDate,
            amount: 500m,
            today: InvoiceTestData.Today,
            nowUtc: createdUtc);

        Assert.Equal(7001, creditNote.Number);
        Assert.Equal(creditNoteDate, creditNote.CreditNoteDate);
        Assert.Equal(createdUtc, creditNote.CreatedAtUtc);
        Assert.Equal(500m, invoice.CreditNotesTotal);
        Assert.Equal(1500m, invoice.OutstandingBalance);
        Assert.Equal(InvoiceStatus.Partial, invoice.Status);
        Assert.True(invoice.HasCreditNotes);
        Assert.Equal(createdUtc, invoice.UpdatedAtUtc);
    }

    [Fact]
    public void AddCreditNote_AccumulatingCreditNotesBelowTotal_KeepsPartialStatus()
    {
        var invoice = InvoiceTestData.Invoice(totalAmount: 2000m);

        invoice.AddCreditNote(7001, InvoiceTestData.Today, 500m, InvoiceTestData.Today, InvoiceTestData.NowUtc);
        invoice.AddCreditNote(7002, InvoiceTestData.Today, 600m, InvoiceTestData.Today, InvoiceTestData.NowUtc);

        Assert.Equal(1100m, invoice.CreditNotesTotal);
        Assert.Equal(900m, invoice.OutstandingBalance);
        Assert.Equal(InvoiceStatus.Partial, invoice.Status);
    }

    [Fact]
    public void AddCreditNote_EqualToRemainingBalance_CancelsInvoiceAndLeavesZeroBalance()
    {
        var invoice = InvoiceTestData.Invoice(totalAmount: 2000m);

        invoice.AddCreditNote(7001, InvoiceTestData.Today, 2000m, InvoiceTestData.Today, InvoiceTestData.NowUtc);

        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
        Assert.Equal(0m, invoice.OutstandingBalance);
    }

    [Fact]
    public void AddCreditNote_ExceedingOutstandingBalance_ThrowsBusinessRule()
    {
        var invoice = InvoiceTestData.Invoice(totalAmount: 2000m);
        invoice.AddCreditNote(7001, InvoiceTestData.Today, 1500m, InvoiceTestData.Today, InvoiceTestData.NowUtc);

        var exception = Assert.Throws<BusinessRuleException>(() =>
            invoice.AddCreditNote(7002, InvoiceTestData.Today, 600m, InvoiceTestData.Today, InvoiceTestData.NowUtc));

        Assert.Equal(DomainErrorCode.CreditNoteExceedsOutstandingBalance, exception.Code);
        Assert.Single(invoice.CreditNotes);
        Assert.Equal(1500m, invoice.CreditNotesTotal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void AddCreditNote_WithNonPositiveAmount_ThrowsBusinessRule(decimal amount)
    {
        var invoice = InvoiceTestData.Invoice();

        var exception = Assert.Throws<BusinessRuleException>(() =>
            invoice.AddCreditNote(7001, InvoiceTestData.Today, amount, InvoiceTestData.Today, InvoiceTestData.NowUtc));

        Assert.Equal(DomainErrorCode.CreditNoteAmountNotPositive, exception.Code);
    }

    [Fact]
    public void AddCreditNote_WithDuplicatedNumber_ThrowsBusinessRule()
    {
        var invoice = InvoiceTestData.Invoice(totalAmount: 2000m);
        invoice.AddCreditNote(7001, InvoiceTestData.Today, 100m, InvoiceTestData.Today, InvoiceTestData.NowUtc);

        var exception = Assert.Throws<BusinessRuleException>(() =>
            invoice.AddCreditNote(7001, InvoiceTestData.Today, 100m, InvoiceTestData.Today, InvoiceTestData.NowUtc));

        Assert.Equal(DomainErrorCode.InvalidInvoiceData, exception.Code);
    }

    [Fact]
    public void AddCreditNote_OnCancelledInvoice_ThrowsAlreadyCancelled()
    {
        var invoice = InvoiceTestData.Invoice(totalAmount: 2000m);
        invoice.AddCreditNote(7001, InvoiceTestData.Today, 2000m, InvoiceTestData.Today, InvoiceTestData.NowUtc);

        var exception = Assert.Throws<BusinessRuleException>(() =>
            invoice.AddCreditNote(7002, InvoiceTestData.Today, 1m, InvoiceTestData.Today, InvoiceTestData.NowUtc));

        Assert.Equal(DomainErrorCode.InvoiceAlreadyCancelled, exception.Code);
    }

    [Fact]
    public void AddCreditNote_OnInconsistentInvoice_ThrowsInconsistentInvoice()
    {
        var invoice = InvoiceTestData.Invoice(totalAmount: 2000m, details: new[]
        {
            InvoiceTestData.Detail(unitPrice: 100m, quantity: 1)
        });

        var exception = Assert.Throws<BusinessRuleException>(() =>
            invoice.AddCreditNote(7001, InvoiceTestData.Today, 10m, InvoiceTestData.Today, InvoiceTestData.NowUtc));

        Assert.Equal(DomainErrorCode.InconsistentInvoice, exception.Code);
    }

    [Fact]
    public void RegisterPayment_SetsPaidStatusAndPaymentDate()
    {
        var invoice = InvoiceTestData.Invoice(paymentDueDate: InvoiceTestData.Today.AddDays(-30));
        Assert.Equal(PaymentStatus.Overdue, invoice.PaymentStatus);

        invoice.RegisterPayment("Transferencia", InvoiceTestData.Today, InvoiceTestData.Today, InvoiceTestData.NowUtc);

        Assert.True(invoice.HasPayment);
        Assert.Equal(PaymentStatus.Paid, invoice.PaymentStatus);
        Assert.Equal("Transferencia", invoice.Payment!.PaymentMethod);
        Assert.Equal(InvoiceTestData.Today, invoice.Payment.PaymentDate);
    }

    [Fact]
    public void RegisterPayment_OnInconsistentInvoice_ThrowsInconsistentInvoice()
    {
        var invoice = InvoiceTestData.Invoice(totalAmount: 2000m, details: new[]
        {
            InvoiceTestData.Detail(unitPrice: 100m, quantity: 1)
        });

        var exception = Assert.Throws<BusinessRuleException>(() =>
            invoice.RegisterPayment("Transferencia", InvoiceTestData.Today, InvoiceTestData.Today, InvoiceTestData.NowUtc));

        Assert.Equal(DomainErrorCode.InconsistentInvoice, exception.Code);
        Assert.False(invoice.HasPayment);
    }

    [Fact]
    public void EnsureIsActive_OnInconsistentInvoice_ThrowsInconsistentInvoice()
    {
        var invoice = InvoiceTestData.Invoice(totalAmount: 2000m, details: new[]
        {
            InvoiceTestData.Detail(unitPrice: 100m, quantity: 1)
        });

        var exception = Assert.Throws<BusinessRuleException>(invoice.EnsureIsActive);

        Assert.Equal(DomainErrorCode.InconsistentInvoice, exception.Code);
    }

    [Fact]
    public void AttachDetails_ThenRefresh_RestoresConsistencyFlag()
    {
        var invoice = InvoiceTestData.Invoice(totalAmount: 2000m, details: new[]
        {
            InvoiceTestData.Detail(unitPrice: 100m, quantity: 1)
        });
        Assert.False(invoice.IsConsistent);

        invoice.AttachDetails(new[] { InvoiceTestData.Detail(unitPrice: 1000m, quantity: 2) });
        invoice.Refresh(InvoiceTestData.Today, InvoiceTestData.NowUtc);

        Assert.True(invoice.IsConsistent);
        Assert.Null(invoice.InconsistencyReason);
        Assert.Equal(InvoiceTestData.NowUtc, invoice.UpdatedAtUtc);
    }

    [Fact]
    public void AttachCreditNotes_ThenRefreshRecalculatesStatusAndBalance()
    {
        var invoice = InvoiceTestData.Invoice(totalAmount: 2000m);

        invoice.AttachCreditNotes(new[]
        {
            RebuildCreditNote(7001, 500m),
            RebuildCreditNote(7002, 700m)
        });
        invoice.RefreshStatuses(InvoiceTestData.Today);

        Assert.Equal(1200m, invoice.CreditNotesTotal);
        Assert.Equal(800m, invoice.OutstandingBalance);
        Assert.Equal(InvoiceStatus.Partial, invoice.Status);
    }

    [Fact]
    public void AttachCreditNotes_WhenCreditExceedsTotal_SaturatesBalanceAtZero()
    {
        var invoice = InvoiceTestData.Invoice(totalAmount: 1000m);

        invoice.AttachCreditNotes(new[] { RebuildCreditNote(7001, 1500m, carrierTotal: 5000m) });
        invoice.RefreshStatuses(InvoiceTestData.Today);

        Assert.Equal(0m, invoice.OutstandingBalance);
        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
    }

    [Fact]
    public void FlagInconsistent_MarksInvoiceOutsideActiveSystem()
    {
        var invoice = InvoiceTestData.Invoice();
        var flagUtc = InvoiceTestData.NowUtc.AddHours(1);

        invoice.FlagInconsistent("desviacion detectada en la rutina SQL", flagUtc);

        Assert.False(invoice.IsConsistent);
        Assert.Equal("desviacion detectada en la rutina SQL", invoice.InconsistencyReason);
        Assert.Equal(flagUtc, invoice.UpdatedAtUtc);
    }

    /// <summary>
    /// Construye una nota de credito a traves del agregado. El constructor de <see cref="CreditNote"/> es
    /// interno a proposito (solo el agregado puede emitirlas), por lo que las pruebas de reconstruccion
    /// desde persistencia usan una factura portadora que si admite el monto.
    /// </summary>
    private static CreditNote RebuildCreditNote(int number, decimal amount, decimal carrierTotal = 2000m)
    {
        var carrier = InvoiceTestData.Invoice(
            number: 9000,
            totalAmount: carrierTotal,
            details: new[] { InvoiceTestData.Detail(unitPrice: carrierTotal, quantity: 1) });

        return carrier.AddCreditNote(number, InvoiceTestData.Today, amount, InvoiceTestData.Today, InvoiceTestData.NowUtc);
    }
}
