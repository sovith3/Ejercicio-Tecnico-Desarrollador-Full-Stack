using System.Text.Json;
using InvoiceHub.Api.Serialization;

namespace InvoiceHub.Api.Tests.Serialization;

/// <summary>
/// Pruebas del formato de montos publicado por la API: numeros decimales sin notacion cientifica y con
/// hasta cinco decimales, tal como los consume el panel chileno.
/// </summary>
public sealed class PlainDecimalJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new PlainDecimalJsonConverter() }
    };

    [Theory]
    [InlineData("0", "0")]
    [InlineData("265019", "265019")]
    [InlineData("1234.5", "1234.5")]
    [InlineData("1234.56789", "1234.56789")]
    [InlineData("0.00001", "0.00001")]
    [InlineData("-1500.25", "-1500.25")]
    public void Write_UsesPlainDecimalNotation(string amount, string expected)
    {
        Assert.Equal(expected, JsonSerializer.Serialize(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), Options));
    }

    [Fact]
    public void Read_RestoresTheAmount()
    {
        var value = JsonSerializer.Deserialize<decimal>("0.00001", Options);

        Assert.Equal(0.00001m, value);
    }
}
