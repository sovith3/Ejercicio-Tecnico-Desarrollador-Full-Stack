namespace InvoiceHub.Application.Common;

/// <summary>
/// Contrato monetario del sistema: pesos chilenos con hasta cinco decimales. El motor de base de datos
/// persiste y agrega los montos como enteros escalados a esa precision (1 peso = 100.000 unidades), lo
/// que garantiza aritmetica exacta -sin errores de redondeo de punto flotante- en las sumas, saldos y
/// comparaciones. La API expone siempre el valor decimal final (265019 o 1234,5) y la interfaz lo muestra
/// con punto de miles y coma decimal.
/// </summary>
public static class Money
{
    /// <summary>Unidades minimas que representa un peso (cinco decimales de precision).</summary>
    public const decimal UnitsPerPeso = 100_000m;

    /// <summary>Decimales maximos del contrato monetario.</summary>
    public const int Decimals = 5;

    /// <summary>
    /// Convierte un monto en pesos a las unidades enteras que almacena el motor, redondeando a cinco
    /// decimales y alejando el punto medio de cero (redondeo comercial).
    /// </summary>
    public static long ToUnits(decimal amount) =>
        (long)decimal.Round(amount * UnitsPerPeso, 0, MidpointRounding.AwayFromZero);

    /// <summary>Convierte las unidades enteras del motor de vuelta al monto en pesos.</summary>
    public static decimal FromUnits(long units) => units / UnitsPerPeso;
}
