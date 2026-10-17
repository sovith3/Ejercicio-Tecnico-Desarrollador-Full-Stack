using InvoiceHub.Domain.Entities;
using InvoiceHub.Domain.Exceptions;

namespace InvoiceHub.Domain.Tests.Entities;

/// <summary>
/// Pruebas de <see cref="InvoicePayment"/>. La fabrica devuelve <c>null</c> cuando el origen de datos no
/// trae pago, lo que mantiene la factura en <c>Pending</c>/<c>Overdue</c>.
/// </summary>
public sealed class InvoicePaymentTests
{
    private static readonly DateTime RecordedUtc = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly PaymentDate = new(2026, 1, 10);

    [Fact]
    public void CreateFromSource_WithMethodAndDate_ReturnsPayment()
    {
        var payment = InvoicePayment.Create("Transferencia", PaymentDate, RecordedUtc);

        Assert.NotNull(payment);
        Assert.Equal("Transferencia", payment!.PaymentMethod);
        Assert.Equal(PaymentDate, payment.PaymentDate);
        Assert.Equal(RecordedUtc, payment.RecordedAtUtc);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("Transferencia", null)]
    public void CreateFromSource_WhenPaymentIsIncomplete_ReturnsNull(string? method, DateOnly? date)
    {
        var payment = InvoicePayment.Create(method, date, RecordedUtc);

        Assert.Null(payment);
    }

    [Fact]
    public void CreateFromSource_TrimsPaymentMethod()
    {
        var payment = InvoicePayment.Create("  Efectivo  ", PaymentDate, RecordedUtc);

        Assert.Equal("Efectivo", payment!.PaymentMethod);
    }

    [Fact]
    public void CreateForRegistration_WithBlankMethod_ThrowsBusinessRule()
    {
        var exception = Assert.Throws<BusinessRuleException>(() =>
            InvoicePayment.Create("   ", PaymentDate, RecordedUtc));

        Assert.Equal(DomainErrorCode.InvalidPayment, exception.Code);
    }

    [Fact]
    public void CreateForRegistration_TrimsPaymentMethod()
    {
        var payment = InvoicePayment.Create("  Cheque  ", PaymentDate, RecordedUtc);

        Assert.Equal("Cheque", payment.PaymentMethod);
    }
}
