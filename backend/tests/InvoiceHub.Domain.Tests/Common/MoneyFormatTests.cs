using InvoiceHub.Domain.Common;

namespace InvoiceHub.Domain.Tests.Common;

/// <summary>
/// Pruebas del formato chileno de los montos que aparecen en los mensajes de negocio: punto para los
/// miles y coma para los decimales, con hasta cinco decimales.
/// </summary>
public sealed class MoneyFormatTests
{
    [Theory]
    [InlineData(0, "0,00")]
    [InlineData(1800, "1.800,00")]
    [InlineData(1234.5, "1.234,50")]
    [InlineData(1234.56789, "1.234,56789")]
    [InlineData(-1500.25, "-1.500,25")]
    [InlineData(265019, "265.019,00")]
    public void Amount_UsesChileanSeparators(decimal value, string expected)
    {
        Assert.Equal(expected, MoneyFormat.Amount(value));
    }
}
