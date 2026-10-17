using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using InvoiceHub.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceHub.Infrastructure.Persistence;

/// <summary>
/// Catalogo de los scripts SQL versionados en <c>database/scripts</c>. Es la unica pieza que conoce su
/// ubicacion: el esquema (01-03), las rutinas de calculo (04-09), la carga masiva (10-13) y las
/// consultas de lectura y reportes (14-21).
/// La numeracion es contigua, del <c>01</c> al <c>21</c>, de modo que el orden alfabetico de los
/// archivos coincide con el orden de aplicacion y no quedan huecos que sugieran scripts faltantes.
/// Los scripts son la fuente de verdad del comportamiento SQL, de modo que pueda revisarse, versionarse
/// y probarse en el motor sin recompilar la aplicacion; este catalogo solo los localiza y los memoriza.
/// </summary>
public sealed class SqlScriptCatalog
{
    /// <summary>Scripts de definicion del esquema, aplicados en este orden en el arranque.</summary>
    public static readonly string[] DdlScripts =
    {
        "01_schema_tables.sql",
        "02_schema_indexes.sql",
        "03_view_invoice_search.sql"
    };

    /// <summary>Rutinas de calculo y de escritura atomica (04-09).</summary>
    public const string RecalculateStatuses = "04_sp_recalculate_statuses.sql";
    public const string AppendCreditNote = "05_sp_append_credit_note.sql";
    public const string ResolveCreditNoteRejection = "06_sp_resolve_credit_note_rejection.sql";
    public const string RegisterPayment = "07_sp_register_payment.sql";
    public const string ResolvePaymentRejection = "08_sp_resolve_payment_rejection.sql";
    public const string OutstandingBalance = "09_sp_get_outstanding_balance.sql";

    /// <summary>Carga masiva del origen en cinco sentencias set-based (10-13).</summary>
    public const string ImportBatchStage = "10_sp_import_batch_stage.sql";
    public const string ImportBatchMerge = "11_sp_import_batch_merge.sql";
    public const string ImportBatchOutcomes = "12_sp_import_batch_outcomes.sql";
    public const string ImportBatchPurge = "13_sp_import_batch_purge.sql";

    /// <summary>Consultas de lectura, reportes y panel (14-21).</summary>
    public const string InvoiceSearch = "14_query_invoice_search.sql";
    public const string InvoiceSearchCount = "15_query_invoice_search_count.sql";
    public const string InvoiceStatusCounts = "16_query_invoice_status_counts.sql";
    public const string PaymentStatusCounts = "17_query_payment_status_counts.sql";
    public const string PaymentStatusSummary = "18_query_payment_status_summary.sql";
    public const string OverdueWithoutActivity = "19_query_overdue_without_activity.sql";
    public const string InconsistentInvoices = "20_query_inconsistent_invoices.sql";
    public const string Dashboard = "21_query_dashboard.sql";

    private const int MaxParentDepth = 6;
    private const string ScriptsFolder = "scripts";
    private const string DatabaseFolder = "database";

    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<string> _directory;
    private readonly DatabaseOptions _options;
    private readonly AppPaths _paths;
    private readonly ILogger<SqlScriptCatalog> _logger;

    public SqlScriptCatalog(
        IOptions<DatabaseOptions> options,
        AppPaths paths,
        ILogger<SqlScriptCatalog> logger)
    {
        _options = options.Value;
        _paths = paths;
        _logger = logger;
        _directory = new Lazy<string>(ResolveDirectory, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Carpeta efectiva que contiene los scripts.</summary>
    public string Directory => _directory.Value;

    /// <summary>Devuelve el contenido de un script (memorizado: los archivos no cambian en ejecucion).</summary>
    public string Read(string fileName) => _cache.GetOrAdd(fileName, Load);

    /// <summary>
    /// Huella del esquema. Se calcula sobre los scripts DDL unicamente: cambiar una consulta o una
    /// rutina no altera la estructura y por lo tanto no debe invalidar la base existente.
    /// </summary>
    public string ComputeSchemaFingerprint()
    {
        var builder = new StringBuilder();
        foreach (var script in DdlScripts)
        {
            builder.Append(script).Append('\n').Append(Read(script)).Append('\n');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return $"schema-{Convert.ToHexString(hash)[..16]}";
    }

    /// <summary>Scripts requeridos que no se encontraron; se usa en el diagnostico de arranque.</summary>
    public IReadOnlyList<string> MissingScripts()
        => RequiredScripts.Where(name => !File.Exists(Path.Combine(Directory, name))).ToList();

    /// <summary>
    /// Todos los scripts que la aplicacion necesita, en orden de aplicacion. Se expone como contrato
    /// verificable: las pruebas comprueban contra la carpeta real que la numeracion sea contigua del 01
    /// al 21, que no falte ninguno y que no sobre ningun archivo.
    /// </summary>
    public static IReadOnlyList<string> RequiredScripts { get; } = DdlScripts.Concat(new[]
    {
        RecalculateStatuses, AppendCreditNote, ResolveCreditNoteRejection, RegisterPayment,
        ResolvePaymentRejection, OutstandingBalance, ImportBatchStage, ImportBatchMerge,
        ImportBatchOutcomes, ImportBatchPurge, InvoiceSearch, InvoiceSearchCount,
        InvoiceStatusCounts, PaymentStatusCounts, PaymentStatusSummary,
        OverdueWithoutActivity, InconsistentInvoices, Dashboard
    }).ToArray();

    private string Load(string fileName)
    {
        var path = Path.Combine(Directory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"No se encontro el script SQL '{fileName}'. Carpeta buscada: '{Directory}'. " +
                "Copie 'database/scripts' junto a la aplicacion o configure 'Database:ScriptsPath'.",
                path);
        }

        return File.ReadAllText(path);
    }

    private string ResolveDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_options.ScriptsPath))
        {
            var configured = Path.IsPathRooted(_options.ScriptsPath!)
                ? _options.ScriptsPath!
                : Path.GetFullPath(Path.Combine(_paths.BasePath, _options.ScriptsPath!));

            _logger.LogInformation("Carpeta de scripts SQL configurada: {Directory}.", configured);
            return configured;
        }

        foreach (var candidate in EnumerateCandidates())
        {
            if (File.Exists(Path.Combine(candidate, DdlScripts[0])))
            {
                _logger.LogInformation("Carpeta de scripts SQL resuelta automaticamente: {Directory}.", candidate);
                return candidate;
            }
        }

        var fallback = Path.Combine(_paths.BasePath, DatabaseFolder, ScriptsFolder);
        _logger.LogWarning(
            "No se encontro la carpeta de scripts SQL en los directorios habituales; se usara {Directory}.",
            fallback);
        return fallback;
    }

    /// <summary>
    /// Candidatos en orden de preferencia: junto a los binarios (salida de compilacion y contenedor) y
    /// luego en cada directorio superior (ejecucion desde el IDE o mediante <c>dotnet run</c>).
    /// </summary>
    private IEnumerable<string> EnumerateCandidates()
    {
        var roots = new[] { AppContext.BaseDirectory, _paths.BasePath, System.IO.Directory.GetCurrentDirectory() }
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
                             Path.Combine(current.FullName, DatabaseFolder, ScriptsFolder),
                             Path.Combine(current.FullName, ScriptsFolder),
                             Path.Combine(current.FullName, "backend", DatabaseFolder, ScriptsFolder)
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
