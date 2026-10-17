namespace InvoiceHub.Infrastructure.Configuration;

/// <summary>Configuracion del archivo JSON de origen de facturas.</summary>
public sealed class SourceDataOptions
{
    public const string SectionName = "SourceData";

    /// <summary>Ruta del archivo JSON (relativa al directorio de contenido si no es absoluta).</summary>
    public string FilePath { get; set; } = Path.Combine("data", "bd_exam_invoices.json");

    /// <summary>Limite defensivo de tamano del archivo que la API acepta cargar.</summary>
    public long MaxFileSizeBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>
    /// Cuando es <c>true</c>, al arrancar la API se importa el archivo JSON si aun no hay datos
    /// cargados. Se usa en desarrollo y en el arranque del contenedor para que la aplicacion quede
    /// operativa sin pasos manuales.
    /// </summary>
    public bool ImportOnStartup { get; set; }

    public string ResolveFilePath(string basePath)
        => Path.IsPathRooted(FilePath) ? FilePath : Path.GetFullPath(Path.Combine(basePath, FilePath));
}

/// <summary>
/// Configuracion del reloj de negocio. Los vencimientos y la mora se evaluan con la fecha de la zona
/// horaria configurada, no con la fecha UTC cruda.
/// </summary>
public sealed class ClockOptions
{
    public const string SectionName = "Clock";

    /// <summary>Zona horaria IANA usada para resolver la fecha de negocio.</summary>
    public string TimeZoneId { get; set; } = "America/Santiago";

    public TimeZoneInfo ResolveTimeZone()
    {
        if (string.IsNullOrWhiteSpace(TimeZoneId))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }
}
