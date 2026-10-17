using InvoiceHub.Application.ReadModels;
using InvoiceHub.Domain.Entities;
using InvoiceHub.Domain.Enums;

namespace InvoiceHub.Application.Tests.Fakes;

/// <summary>
/// Datos de prueba de la capa de aplicacion. La fecha de negocio y la hora UTC son fijas para que las
/// aserciones sobre mora, estados y marcas de tiempo no dependan del reloj real.
/// </summary>
internal static class TestRows
{
    public static readonly DateOnly Today = new(2026, 1, 15);

    public static readonly DateTime NowUtc = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Fila de la vista de busqueda (montos en unidades de 1/100000 de peso, saldo derivado por omision).</summary>
    public static InvoiceSearchRow Row(
        int number = 1000,
        long invoiceId = 1,
        long totalAmountUnits = 200_000_000,
        long creditNotesTotalUnits = 0,
        long? outstandingBalanceUnits = null,
        int creditNoteCount = 0,
        InvoiceStatus invoiceStatus = InvoiceStatus.Issued,
        PaymentStatus paymentStatus = PaymentStatus.Pending,
        bool isConsistent = true,
        string? inconsistencyReason = null,
        bool hasPayment = false,
        string? paymentMethod = null,
        DateOnly? paymentDate = null,
        int daysOverdue = 0,
        int daysToDue = 30,
        string customerRun = "12345678-9",
        string customerName = "Cliente Demo",
        string customerEmail = "cliente@demo.cl",
        DateOnly? invoiceDate = null,
        DateOnly? paymentDueDate = null)
        => new()
        {
            InvoiceId = invoiceId,
            Number = number,
            InvoiceDate = invoiceDate ?? Today.AddDays(-10),
            PaymentDueDate = paymentDueDate ?? Today.AddDays(daysToDue),
            DaysToDue = daysToDue,
            TotalAmountUnits = totalAmountUnits,
            CreditNotesTotalUnits = creditNotesTotalUnits,
            OutstandingBalanceUnits = outstandingBalanceUnits ?? totalAmountUnits - creditNotesTotalUnits,
            CreditNoteCount = creditNoteCount,
            InvoiceStatus = invoiceStatus,
            PaymentStatus = paymentStatus,
            IsConsistent = isConsistent,
            InconsistencyReason = inconsistencyReason,
            HasPayment = hasPayment,
            PaymentMethod = paymentMethod,
            PaymentDate = paymentDate,
            DaysOverdue = daysOverdue,
            CustomerRun = customerRun,
            CustomerName = customerName,
            CustomerEmail = customerEmail
        };

    public static InvoiceDetail Line(
        string productName = "Producto",
        decimal unitPrice = 1000m,
        int quantity = 2,
        decimal? subtotal = null)
        => new(productName, unitPrice, quantity, subtotal ?? unitPrice * quantity);

    /// <summary>Factura consistente por omision (total 2.000 = una linea de 1.000 x 2).</summary>
    public static Invoice Invoice(
        int number = 1000,
        decimal totalAmount = 2000m,
        IEnumerable<InvoiceDetail>? details = null,
        InvoicePayment? payment = null,
        DateOnly? paymentDueDate = null,
        int daysToDue = 30,
        InvoiceStatus? declaredStatus = null,
        PaymentStatus? declaredPaymentStatus = null,
        string customerRun = "12345678-9",
        string customerName = "Cliente Demo",
        string customerEmail = "cliente@demo.cl")
        => new(
            number,
            Today.AddDays(-10),
            totalAmount,
            daysToDue,
            paymentDueDate ?? Today.AddDays(daysToDue),
            new Customer(customerRun, customerName, customerEmail, NowUtc),
            details ?? new[] { Line() },
            payment,
            Today,
            NowUtc,
            declaredStatus,
            declaredPaymentStatus);
}
