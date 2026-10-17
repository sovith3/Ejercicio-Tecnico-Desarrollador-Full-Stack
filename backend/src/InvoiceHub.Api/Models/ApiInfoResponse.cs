namespace InvoiceHub.Api.Models;

/// <summary>Respuesta informativa de la raiz de la API, con los enlaces utiles para el cliente.</summary>
public sealed record ApiInfoResponse(
    string Name,
    string Version,
    string Environment,
    string Description,
    IReadOnlyDictionary<string, string> Endpoints);

/// <summary>Informe de salud de la API y de sus dependencias.</summary>
public sealed record HealthReportResponse(
    string Status,
    DateTime CheckedAtUtc,
    double TotalDurationMilliseconds,
    IReadOnlyList<HealthEntryResponse> Checks);

/// <summary>Resultado de una verificacion de salud individual.</summary>
public sealed record HealthEntryResponse(
    string Name,
    string Status,
    string? Description,
    double DurationMilliseconds,
    IReadOnlyDictionary<string, string?> Data);

/// <summary>
/// Endpoint de diagnostico: permite comprobar sin abrir la base de datos que esquema se aplico, que
/// cache esta activa y cual fue la ultima importacion del archivo JSON.
/// </summary>
public sealed record DiagnosticsResponse(
    string Application,
    string Version,
    string Environment,
    string MachineName,
    DateTime UtcNow,
    DateOnly BusinessDate,
    string TimeZoneId,
    DiagnosticsDatabase Database,
    DiagnosticsCache Cache,
    DiagnosticsSourceFile SourceFile,
    DiagnosticsImport? LastImport,
    DiagnosticsUser User);

public sealed record DiagnosticsDatabase(
    string FilePath,
    long SizeInBytes,
    string SchemaFingerprint,
    int InvoiceCount);

/// <summary>
/// Estado de la cache efectiva. <paramref name="Connected"/> distingue el Redis realmente accesible del
/// respaldo en memoria, y <paramref name="PasswordConfigured"/> confirma (sin exponer el valor) que la
/// contrasena del entorno llego a la aplicacion.
/// </summary>
public sealed record DiagnosticsCache(
    string Provider,
    bool RedisEnabled,
    string InstanceName,
    int InvoiceTimeToLiveSeconds,
    int ReportTimeToLiveSeconds,
    bool Connected,
    bool PasswordConfigured,
    string Endpoint);

public sealed record DiagnosticsSourceFile(
    string FilePath,
    string FileName,
    bool Exists,
    long SizeInBytes,
    DateTime? ModifiedAtUtc);

public sealed record DiagnosticsImport(
    long Id,
    string SourceFile,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    string Status,
    int TotalRecords,
    int ImportedRecords,
    int InconsistentRecords,
    int SkippedRecords,
    int FailedRecords,
    int DurationMilliseconds,
    IReadOnlyList<string> Issues);

public sealed record DiagnosticsUser(string? Username, string? Role, bool IsAuthenticated);
