namespace InvoiceHub.Api.Configuration;

/// <summary>
/// Opciones propias del host HTTP (seccion <c>Api</c>): permiten apagar la redireccion a HTTPS o el
/// servido de la interfaz compilada sin recompilar, algo util al ejecutar tras un proxy inverso que ya
/// termina TLS o cuando la interfaz se sirve desde otro origen.
/// </summary>
public sealed class ApiHostOptions
{
    public const string SectionName = "Api";

    /// <summary>Redirige las solicitudes HTTP a HTTPS. Por omision desactivado (contenedor y proxy inverso).</summary>
    public bool UseHttpsRedirection { get; set; }

    /// <summary>Sirve la interfaz compilada desde <c>wwwroot</c> y devuelve <c>index.html</c> en las rutas no reconocidas.</summary>
    public bool ServeStaticFiles { get; set; } = true;

    /// <summary>Publica la interfaz de Swagger en <c>/swagger</c>.</summary>
    public bool SwaggerEnabled { get; set; } = true;
}
