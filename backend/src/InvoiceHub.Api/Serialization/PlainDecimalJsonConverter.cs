using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InvoiceHub.Api.Serialization;

/// <summary>
/// Escribe los montos como numeros decimales sin notacion cientifica (0.00001 en lugar de 1E-05),
/// porque el contrato monetario son pesos chilenos con hasta cinco decimales y el panel los presenta
/// con punto de miles y coma decimal. Los ceros finales no se envian: 265019 sigue siendo un entero.
/// </summary>
public sealed class PlainDecimalJsonConverter : JsonConverter<decimal>
{
    /// <summary>Hasta cinco decimales, sin ceros a la derecha ni exponente.</summary>
    private const string Pattern = "0.#####";

    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        => writer.WriteRawValue(
            value.ToString(Pattern, CultureInfo.InvariantCulture),
            skipInputValidation: true);
}
