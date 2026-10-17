using InvoiceHub.Domain.Entities;
using InvoiceHub.Domain.Enums;

namespace InvoiceHub.Domain.Tests.Builders;

/// <summary>
/// Datos reutilizables de las pruebas de dominio. Centraliza la fecha de negocio y la hora UTC para
/// que las aserciones sobre vencimientos y estados no dependan del reloj real.
/// </summary>
internal static class InvoiceTestData
{
    /// <summary>Fecha de negocio ficticia usada en todas las pruebas.</summary>
    public static readonly DateOnly Today = new(2026, 1, 15);

    /// <summary>Marca de tiempo UTC ficticia usada en todas las pruebas.</summary>
    public static readonly DateTime NowUtc = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    public static Customer Customer(
        string run = "12345678-9",
        string name = "Cliente Demo",
        string email = "cliente@demo.cl")
        => new(run, name, email, NowUtc);

    public static InvoiceDetail Detail(
        string productName = "Producto",
        decimal unitPrice = 1000m,
        int quantity = 2,
        decimal? subtotal = null)
        => new(productName, unitPrice, quantity, subtotal ?? unitPrice * quantity);

    /// <summary>
    /// Construye una factura consistente por omision (total 2.000 = una linea de 1.000 x 2) para que
    /// cada prueba solo describa el aspecto que le interesa.
    /// </summary>
    public static Invoice Invoice(
        int number = 1000,
        decimal totalAmount = 2000m,
        IEnumerable<InvoiceDetail>? details = null,
        DateOnly? invoiceDate = null,
        DateOnly? paymentDueDate = null,
        int daysToDue = 30,
        InvoicePayment? payment = null,
        DateOnly? today = null,
        InvoiceStatus? declaredStatus = null,
        PaymentStatus? declaredPaymentStatus = null,
        Customer? customer = null)
        => new(
            number,
            invoiceDate ?? Today.AddDays(-10),
            totalAmount,
            daysToDue,
            paymentDueDate ?? Today.AddDays(20),
            customer ?? Customer(),
            details ?? new[] { Detail() },
            payment,
            today ?? Today,
            NowUtc,
            declaredStatus,
            declaredPaymentStatus);
}
