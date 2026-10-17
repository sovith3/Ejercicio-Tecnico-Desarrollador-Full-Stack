using System.Data;
using Dapper;

namespace InvoiceHub.Infrastructure.Persistence;

/// <summary>
/// Registro de conversiones que Dapper necesita para los tipos de valor usados por el esquema
/// (fechas de negocio como TEXT ISO). Se ejecuta una unica vez por proceso y es idempotente.
/// </summary>
public static class DapperRules
{
    private static int _registered;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        SqlMapper.AddTypeHandler(new DateOnlyHandler());
    }

    /// <summary>
    /// Dapper registra automaticamente el handler tambien para <c>DateOnly?</c> cuando el tipo es un
    /// value type, por lo que las columnas anulables (por ejemplo la fecha de pago) funcionan igual.
    /// </summary>
    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override DateOnly Parse(object value) => value switch
        {
            string text => DateOnly.ParseExact(
                SqliteValueFormat.NormalizeDateText(text),
                SqliteValueFormat.DatePattern,
                System.Globalization.CultureInfo.InvariantCulture),
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            DateOnly dateOnly => dateOnly,
            _ => throw new DataException($"No es posible convertir el valor '{value}' a DateOnly.")
        };

        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = SqliteValueFormat.ToStorage(value);
        }
    }
}

/// <summary>
/// Formato de almacenamiento de las columnas del esquema. Se centraliza porque los scripts SQL y las
/// entidades administradas por EF deben escribir exactamente el mismo formato: de lo contrario una
/// fecha creada por una rutina SQL no podria ser leida por EF (y viceversa).
/// </summary>
public static class SqliteValueFormat
{
    /// <summary>Formato de las columnas de fecha de negocio (compatible con date()/julianday()).</summary>
    public const string DatePattern = "yyyy-MM-dd";

    /// <summary>Formato de las columnas de fecha/hora UTC (identico al que usa el proveedor de EF Core).</summary>
    public const string DateTimePattern = "yyyy-MM-dd HH:mm:ss.fffffff";

    public static string ToStorage(DateOnly value)
        => value.ToString(DatePattern, System.Globalization.CultureInfo.InvariantCulture);

    public static string ToStorage(DateTime value)
        => (value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value)
            .ToString(DateTimePattern, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Reduce el texto a los primeros 10 caracteres (yyyy-MM-dd) antes de parsearlo.</summary>
    public static string NormalizeDateText(string text)
        => text.Length >= 10 ? text[..10] : text;
}
