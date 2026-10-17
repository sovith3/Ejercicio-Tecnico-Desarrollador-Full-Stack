using System.Text.Json;
using System.Text.Json.Serialization;
using InvoiceHub.Application.Ports;
using InvoiceHub.Infrastructure.Configuration;
using InvoiceHub.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceHub.Infrastructure.SourceData;

/// <summary>
/// Adaptador de entrada de datos: lee el archivo JSON de facturas configurado en <c>SourceData</c>.
/// </summary>
/// <remarks>
/// Dos decisiones relevantes:
/// <list type="bullet">
/// <item><b>Deserializacion tolerante.</b> Se admiten comentarios, comas finales, numeros en formato
/// cadena y un limite defensivo de tamano, porque el archivo proviene de un tercero y una carga completa
/// no debe caer por un detalle de formato; los registros invalidos los descarta la capa de aplicacion.</item>
/// <item><b>Resolucion tolerante de la ruta.</b> Una ruta relativa se interpreta respecto del directorio
/// de contenido (ejecucion con <c>dotnet run</c>) y, si ahi no existe el archivo, se busca junto a los
/// binarios y en los directorios superiores; asi el mismo binario funciona en el IDE, en
/// <c>dotnet test</c> y dentro del contenedor sin recompilar.</item>
/// </list>
/// </remarks>
public sealed class InvoiceSourceReader : IInvoiceSourceReader
{
    private const int MaxParentDepth = 6;
    private const int StreamBufferSize = 64 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly SourceDataOptions _options;
    private readonly AppPaths _paths;
    private readonly ILogger<InvoiceSourceReader> _logger;
    private readonly Lazy<string> _resolvedPath;

    public InvoiceSourceReader(
        IOptions<SourceDataOptions> options,
        AppPaths paths,
        ILogger<InvoiceSourceReader> logger)
    {
        _options = options.Value;
        _paths = paths;
        _logger = logger;
        _resolvedPath = new Lazy<string>(ResolveFilePath, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Ruta efectiva del archivo de origen (se expone para el diagnostico y las pruebas).</summary>
    public string FilePath => _resolvedPath.Value;

    public SourceFileDescriptor Describe()
    {
        var info = new FileInfo(FilePath);
        return info.Exists
            ? new SourceFileDescriptor(info.FullName, true, info.Length, info.LastWriteTimeUtc)
            : new SourceFileDescriptor(info.FullName, false, 0, null);
    }

    /// <summary>
    /// Lee y deserializa el archivo completo. Los fallos de contrato (archivo ausente, demasiado grande o
    /// ilegible) se propagan como excepciones descriptivas: la importacion no puede continuar sin origen.
    /// </summary>
    public async Task<InvoiceSourceDocument> ReadAsync(CancellationToken cancellationToken)
    {
        var descriptor = Describe();
        if (!descriptor.Exists)
        {
            throw new FileNotFoundException(
                $"No se encontro el archivo JSON de origen de facturas. Ruta esperada: '{descriptor.FilePath}'. " +
                "Configure 'SourceData:FilePath' o copie el archivo junto a la aplicacion.",
                descriptor.FilePath);
        }

        if (descriptor.SizeInBytes > _options.MaxFileSizeBytes)
        {
            throw new InvalidDataException(
                $"El archivo de origen '{descriptor.FilePath}' ocupa {descriptor.SizeInBytes} bytes y supera el " +
                $"limite configurado de {_options.MaxFileSizeBytes} bytes ('SourceData:MaxFileSizeBytes').");
        }

        try
        {
            await using var stream = new FileStream(
                descriptor.FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                StreamBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var payload = await JsonSerializer
                .DeserializeAsync<SourceInvoiceFile>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            if (payload is null)
            {
                throw new InvalidDataException(
                    $"El archivo de origen '{descriptor.FilePath}' esta vacio o no contiene un objeto JSON.");
            }

            _logger.LogInformation(
                "Origen de datos leido: {File} ({Size} bytes, {Count} facturas).",
                descriptor.FilePath,
                descriptor.SizeInBytes,
                payload.Invoices.Count);

            return new InvoiceSourceDocument(descriptor.FilePath, payload.Invoices);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"El archivo de origen '{descriptor.FilePath}' no respeta el formato JSON esperado " +
                $"(linea {exception.LineNumber}, posicion {exception.BytePositionInLine}).",
                exception);
        }
    }

    /// <summary>
    /// Resuelve la ruta del archivo: la configurada, o la primera coincidencia encontrada junto a los
    /// binarios o en los directorios superiores. Si no existe ninguna se devuelve la configurada para que
    /// el mensaje de error señale la ruta que el operador debe revisar.
    /// </summary>
    private string ResolveFilePath()
    {
        var configured = _options.ResolveFilePath(_paths.BasePath);
        if (File.Exists(configured))
        {
            return configured;
        }

        foreach (var candidate in EnumerateCandidates())
        {
            if (File.Exists(candidate))
            {
                _logger.LogInformation(
                    "El archivo de origen configurado ({Configured}) no existe; se usara {Candidate}.",
                    configured,
                    candidate);
                return candidate;
            }
        }

        _logger.LogWarning(
            "No se encontro el archivo de origen de facturas. Ruta esperada: {Configured}. " +
            "La importacion fallara hasta que el archivo este disponible.",
            configured);

        return configured;
    }

    /// <summary>Candidatos en el mismo orden de preferencia que usa <see cref="SqlScriptCatalog"/>.</summary>
    private IEnumerable<string> EnumerateCandidates()
    {
        var fileName = Path.GetFileName(_options.FilePath);
        var roots = new[] { AppContext.BaseDirectory, _paths.BasePath, Directory.GetCurrentDirectory() }
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var current = new DirectoryInfo(root);
            for (var depth = 0; current is not null && depth <= MaxParentDepth; depth++, current = current.Parent)
            {
                foreach (var candidate in new[]
                         {
                             Path.GetFullPath(Path.Combine(current.FullName, _options.FilePath)),
                             Path.Combine(current.FullName, "data", fileName),
                             Path.Combine(current.FullName, fileName)
                         })
                {
                    if (seen.Add(candidate))
                    {
                        yield return candidate;
                    }
                }
            }
        }
    }
}
