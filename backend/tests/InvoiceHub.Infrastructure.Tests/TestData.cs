using InvoiceHub.Domain.Entities;

namespace InvoiceHub.Infrastructure.Tests;

/// <summary>
/// Datos de prueba de infraestructura. La fecha de negocio y la hora UTC son fijas para que las aserciones
/// sobre mora, estados y marcas de tiempo no dependan del reloj real de la maquina.
/// </summary>
internal static class TestData
{
    public static readonly DateOnly Today = new(2026, 1, 15);

    public static readonly DateTime NowUtc = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Fecha de negocio vigente en la zona configurada por el host de pruebas (<c>America/Santiago</c>), que es
    /// la misma que resuelve el reloj de la aplicacion. Las pruebas relativas a "hoy" deben partir de esta
    /// fecha: usar la fecha UTC cruda desplaza un dia cuando en la zona de negocio todavia no cambio el dia.
    /// </summary>
    public static DateOnly BusinessToday()
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Santiago")));

    public static Customer Customer(
        string run = "12345678-9",
        string name = "Cliente Demo",
        string email = "cliente@demo.cl")
        => new(run, name, email, NowUtc);

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
        DateOnly? invoiceDate = null,
        DateOnly? paymentDueDate = null,
        int daysToDue = 30,
        InvoicePayment? payment = null,
        Customer? customer = null)
        => new(
            number,
            invoiceDate ?? Today.AddDays(-10),
            totalAmount,
            daysToDue,
            paymentDueDate ?? Today.AddDays(daysToDue),
            customer ?? Customer(run: $"1000000{number % 1000}-1", email: $"cliente{number}@demo.cl"),
            details ?? new[] { Line() },
            payment,
            Today,
            NowUtc);
}
