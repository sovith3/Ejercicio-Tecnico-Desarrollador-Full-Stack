namespace InvoiceHub.Domain.Common;

/// <summary>
/// Validaciones de invariantes basicas reutilizadas por entidades y servicios de dominio.
/// Las reglas de negocio propiamente tales se expresan con <see cref="Exceptions.BusinessRuleException"/>.
/// </summary>
public static class Guard
{
    public static string NotEmpty(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"El campo '{parameterName}' es obligatorio.", parameterName);
        }

        return value.Trim();
    }

    public static T NotNull<T>(T? value, string parameterName) where T : class
        => value ?? throw new ArgumentNullException(parameterName);

    public static decimal NotNegative(decimal value, string parameterName)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "El valor no puede ser negativo.");
        }

        return value;
    }

    public static int NotNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "El valor no puede ser negativo.");
        }

        return value;
    }

    public static int Positive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "El valor debe ser mayor que cero.");
        }

        return value;
    }
}
