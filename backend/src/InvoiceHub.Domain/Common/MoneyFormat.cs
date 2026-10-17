using System.Globalization;

namespace InvoiceHub.Domain.Common;

/// <summary>
/// Formato legible de los montos en los mensajes de negocio, con la convencion chilena: punto para los
/// miles y coma para los decimales (1.234.567,89). Se muestran entre dos y cinco decimales, de modo que
/// la precision del contrato monetario nunca se pierda al describir una diferencia.
/// </summary>
public static class MoneyFormat
{
    /// <summary>Patron de pesos chilenos: separador de miles y hasta cinco decimales.</summary>
    private const string Pattern = "#,##0.00#####";

    private static readonly NumberFormatInfo ChileanNumberFormat = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
        NumberGroupSizes = new[] { 3 },
        NegativeSign = "-"
    };

    /// <summary>Formatea un monto en pesos: <c>1800</c> -> <c>1.800,00</c>; <c>1234.56789</c> -> <c>1.234,56789</c>.</summary>
    public static string Amount(decimal value) => value.ToString(Pattern, ChileanNumberFormat);
}
