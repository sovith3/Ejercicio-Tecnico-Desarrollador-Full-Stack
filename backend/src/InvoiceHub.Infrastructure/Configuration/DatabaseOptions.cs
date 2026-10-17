using Microsoft.Data.Sqlite;
using StackExchange.Redis;


namespace InvoiceHub.Infrastructure.Configuration;

/// <summary>
/// Configuracion de la base de datos SQLite. La cadena de conexion puede informarse completa o
/// derivarse del archivo, que se resuelve relativo al directorio de contenido de la API.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Cadena de conexion explicita. Tiene prioridad sobre <see cref="FilePath"/>.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Ruta del archivo SQLite (relativa al directorio de contenido si no es absoluta).</summary>
    public string FilePath { get; set; } = Path.Combine("data", "invoicehub.db");

    /// <summary>Carpeta de los scripts SQL. Si es nula se resuelve automaticamente.</summary>
    public string? ScriptsPath { get; set; }

    /// <summary>
    /// Cuando es <c>true</c>, si la huella de los scripts de esquema cambia el inicializador elimina el
    /// archivo y lo recrea. Es util en desarrollo/demo porque el origen de datos se reimporta.
    /// </summary>
    public bool RecreateOnSchemaChange { get; set; } = true;

    /// <summary>Crea el usuario administrador informado en <c>Security</c> cuando la tabla esta vacia.</summary>
    public bool SeedAdminUser { get; set; } = true;

    public string ResolveFilePath(string basePath)
        => Path.IsPathRooted(FilePath) ? FilePath : Path.GetFullPath(Path.Combine(basePath, FilePath));

    /// <summary>
    /// Cadena de conexion efectiva. Se fija <c>Foreign Keys=True</c> para que el motor valide las
    /// relaciones y un tiempo de espera amplio para tolerar escrituras concurrentes.
    /// </summary>
    public string ResolveConnectionString(string basePath)
    {
        if (!string.IsNullOrWhiteSpace(ConnectionString))
        {
            return ConnectionString!;
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = ResolveFilePath(basePath),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            ForeignKeys = true,
            DefaultTimeout = 30
        };

        return builder.ToString();
    }
}

/// <summary>
/// Configuracion del almacen de cache. Redis es el proveedor previsto; si no esta disponible (o no se
/// configura) el adaptador cae automaticamente a memoria para no degradar la disponibilidad.
/// </summary>
/// <remarks>
/// La conexion se resuelve con una jerarquia explicita, pensada para que el entorno (docker compose,
/// Kubernetes, App Service) entregue un unico valor sin reescribir <c>appsettings.json</c>:
/// <list type="number">
/// <item><description><c>ConnectionStrings:RedisConnection</c> - en variable de entorno
/// <c>ConnectionStrings__RedisConnection</c> por la jerarquia de doble guion bajo de .NET.</description></item>
/// <item><description><c>Cache:ConnectionString</c> - cadena completa en la configuracion de la
/// aplicacion.</description></item>
/// <item><description><c>Cache:Host</c> y <c>Cache:Port</c> - se componen cuando no hay cadena.</description></item>
/// </list>
/// Los campos <see cref="User"/>, <see cref="Password"/>, <see cref="Database"/> y <see cref="UseSsl"/> se
/// aplican siempre sobre la cadena resultante: la contrasena se declara una sola vez (por ejemplo
/// <c>REDIS_PASSWORD</c> en el <c>.env</c>) y no hay que repetirla dentro de la cadena.
/// </remarks>
public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>Nombre estandar de la cadena de conexion de Redis (<c>ConnectionStrings:RedisConnection</c>).</summary>
    public const string ConnectionStringName = "RedisConnection";

    /// <summary>Proveedor: <c>Redis</c> o <c>Memory</c>.</summary>
    public string Provider { get; set; } = "Redis";

    /// <summary>Cadena de conexion completa; tiene prioridad sobre <see cref="Host"/> y <see cref="Port"/>.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Servidor de Redis cuando no se informa una cadena de conexion.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>Puerto de Redis cuando no se informa una cadena de conexion.</summary>
    public int Port { get; set; } = 6379;

    /// <summary>Usuario ACL (Redis 6+). Vacio cuando el servidor solo usa <c>requirepass</c>.</summary>
    public string? User { get; set; }

    /// <summary>Contrasena de Redis (<c>AUTH</c> / <c>requirepass</c>). Nunca se registra en los logs.</summary>
    public string? Password { get; set; }

    /// <summary>Base de datos logica de Redis.</summary>
    public int Database { get; set; }

    /// <summary>Activa TLS hacia el servidor de cache.</summary>
    public bool UseSsl { get; set; }

    /// <summary>Prefijo opcional cuando se comparte una instancia de Redis con otras aplicaciones.</summary>
    public string InstanceName { get; set; } = string.Empty;

    /// <summary>Vigencia (segundos) de las entradas de busqueda y detalle: 30 s segun las bases del examen.</summary>
    public int InvoiceTimeToLiveSeconds { get; set; } = 30;

    /// <summary>Vigencia (segundos) de los reportes y del panel.</summary>
    public int ReportTimeToLiveSeconds { get; set; } = 60;

    /// <summary>Si es <c>false</c> se usa memoria incluso cuando el proveedor configurado es Redis.</summary>
    public bool UseRedis { get; set; } = true;

    public bool IsRedisEnabled =>
        UseRedis && string.Equals(Provider, "Redis", StringComparison.OrdinalIgnoreCase);

    /// <summary>Indica si hay contrasena configurada. Se expone en el diagnostico; el valor nunca se publica.</summary>
    public bool HasPassword => !string.IsNullOrWhiteSpace(Password);

    public TimeSpan InvoiceTimeToLive => TimeSpan.FromSeconds(Math.Clamp(InvoiceTimeToLiveSeconds, 1, 3600));

    public TimeSpan ReportTimeToLive => TimeSpan.FromSeconds(Math.Clamp(ReportTimeToLiveSeconds, 1, 3600));

    /// <summary>
    /// Aplica la cadena de conexion inyectada por el entorno (<c>ConnectionStrings:RedisConnection</c>) sin
    /// pisar la configuracion local cuando el valor llega vacio o ausente.
    /// </summary>
    public void OverrideConnectionString(string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            ConnectionString = connectionString.Trim();
        }
    }

    /// <summary>
    /// Opciones de conexion efectivas para StackExchange.Redis: endpoint, credenciales, base de datos logica
    /// y tolerancia a fallos de arranque (la cache nunca debe impedir que la API arranque).
    /// </summary>
    public ConfigurationOptions ResolveConfiguration()
    {
        var configuration = string.IsNullOrWhiteSpace(ConnectionString)
            ? new ConfigurationOptions { EndPoints = { { Host, Port } } }
            : ConfigurationOptions.Parse(ConnectionString!);

        if (!string.IsNullOrWhiteSpace(User))
        {
            configuration.User = User!.Trim();
        }

        if (HasPassword)
        {
            configuration.Password = Password;
        }

        configuration.DefaultDatabase = Database;
        configuration.Ssl = UseSsl;
        configuration.AbortOnConnectFail = false;
        configuration.ConnectRetry = 1;
        configuration.ConnectTimeout = Math.Max(configuration.ConnectTimeout, 2_000);

        return configuration;
    }

    /// <summary>Descripcion del destino sin credenciales, apta para logs y para el endpoint de diagnostico.</summary>
    /// <remarks>Es tolerante a una cadena mal formada: el diagnostico nunca debe romperse por la configuracion.</remarks>
    public string DescribeEndpoint()
    {
        try
        {
            return ResolveConfiguration().ToString(includePassword: false);
        }
        catch (Exception exception) when (exception is ArgumentException
                                              or FormatException
                                              or InvalidOperationException)
        {
            return "(configuracion de Redis invalida)";
        }
    }
}
