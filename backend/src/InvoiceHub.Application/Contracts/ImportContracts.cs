namespace InvoiceHub.Application.Contracts;

/// <summary>Resumen de la ejecucion de importacion desde el archivo JSON.</summary>
public sealed record ImportSummaryDto(
    string SourceFile,
    DateTime StartedAtUtc,
    DateTime FinishedAtUtc,
    int TotalRecords,
    int Imported,
    int Inconsistent,
    int Skipped,
    int Failed,
    int DurationMilliseconds,
    string Status,
    IReadOnlyList<string> Issues);

/// <summary>Datos de la ultima importacion registrada.</summary>
public sealed record ImportRunDto(
    long Id,
    string SourceFile,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    int TotalRecords,
    int ImportedRecords,
    int InconsistentRecords,
    int SkippedRecords,
    int FailedRecords,
    string Status,
    int DurationMilliseconds,
    IReadOnlyList<string> Issues);

/// <summary>Estado del origen de datos JSON configurado.</summary>
public sealed record SourceFileStatusDto(
    string FilePath,
    bool Exists,
    long? SizeInBytes,
    DateTime? ModifiedAtUtc,
    int PersistedInvoices,
    ImportRunDto? LastRun);
