using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace InvoiceHub.Api.Tests.Support;

/// <summary>
/// Host de la API para las pruebas de integracion. Levanta el arranque real (<c>Program</c>) con una base
/// SQLite temporal, los scripts SQL del repositorio y la importacion del archivo del enunciado habilitada,
/// de modo que los endpoints se ejerciten con el mismo esquema, las mismas rutinas y los mismos datos que
/// en ejecucion.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Cantidad de facturas del archivo <c>bd_exam_invoices.json</c>.</summary>
    public const int ImportedInvoiceCount = 50;

    /// <summary>
    /// Facturas del sistema activo: el archivo contiene registros cuyo total declarado no coincide con la
    /// suma de sus subtotales, por lo que quedan excluidos de la busqueda y de los reportes de cartera.
    /// </summary>
    public const int ActiveInvoiceCount = 48;

    private static readonly string RepositoryRoot = ResolveRepositoryRoot();

    public static string ScriptsPath { get; } = Path.Combine(RepositoryRoot, "backend", "database", "scripts");

    public static string SourceFilePath { get; } = Path.Combine(RepositoryRoot, "bd_exam_invoices.json");

    private readonly string _rootPath =
        Path.Combine(Path.GetTempPath(), "invoicehub-api-tests", Guid.NewGuid().ToString("N"));

    public ApiFactory() => Directory.CreateDirectory(_rootPath);

    public string DatabaseFilePath => Path.Combine(_rootPath, "invoicehub.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Database:FilePath"] = DatabaseFilePath,
                ["Database:ScriptsPath"] = ScriptsPath,
                ["SourceData:FilePath"] = SourceFilePath,
                ["SourceData:ImportOnStartup"] = "true",
                ["Cache:Provider"] = "Memory",
                ["Cache:UseRedis"] = "false",
                ["Clock:TimeZoneId"] = "America/Santiago",
                ["Security:RequireAuthentication"] = "false",
                ["Api:SwaggerEnabled"] = "false",
                ["Api:ServeStaticFiles"] = "false",
                ["Logging:LogLevel:Default"] = "Warning"
            }));
    }

    /// <summary>Numero de la primera factura importada, ordenada por numero ascendente.</summary>
    public static async Task<int> FirstInvoiceNumberAsync(HttpClient client)
    {
        var page = await GetJsonAsync(client, "/api/invoices?page=1&pageSize=1&sortBy=number&sortDirection=asc");
        return page.GetProperty("items")[0].GetProperty("number").GetInt32();
    }

    /// <summary>Autentica al usuario administrador sembrado en el arranque y devuelve el token de acceso.</summary>
    public static async Task<string> LoginAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = AdminUsername, password = AdminPassword });

        response.EnsureSuccessStatusCode();

        var body = await GetJsonAsync(response);
        return body.GetProperty("accessToken").GetString()!;
    }

    public static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
        => await GetJsonAsync(await client.GetAsync(url));

    public static async Task<JsonElement> GetJsonAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Credenciales del administrador sembrado por el inicializador de esquema.</summary>
    public const string AdminUsername = "admin";

    public static string AdminPassword => "InvoiceHub.2026";

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_rootPath))
                {
                    Directory.Delete(_rootPath, recursive: true);
                }

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "bd_exam_invoices.json")) &&
                Directory.Exists(Path.Combine(current.FullName, "backend", "database", "scripts")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            $"No se encontro la raiz del repositorio desde '{AppContext.BaseDirectory}'.");
    }
}
