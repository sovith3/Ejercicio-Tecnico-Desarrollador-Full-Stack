using InvoiceHub.Domain.Enums;

namespace InvoiceHub.Domain.Entities;

/// <summary>
/// Bitacora de una ejecucion de importacion desde el archivo JSON. Permite auditar que se cargo,
/// cuando y con que incidencias.
/// </summary>
public sealed class ImportRun
{
    /// <summary>Cantidad maxima de incidencias que se conservan en memoria (y por lo tanto en la bitacora).</summary>
    public const int MaxIssues = 200;

    private readonly List<ImportRunIssue> _issueEntries = new();

    /// <summary>Secuencia global de incidencias: permite ordenar y auditar aunque se acote la bitacora.</summary>
    private int _issueSequence;

    private ImportRun()
    {
        SourceFileName = string.Empty;
    }

    public ImportRun(string sourceFileName, DateTime startedAtUtc)
    {
        SourceFileName = string.IsNullOrWhiteSpace(sourceFileName) ? "desconocido" : sourceFileName.Trim();
        StartedAtUtc = startedAtUtc;
        Status = ImportRunStatus.Running;
        AddIssue($"Ejecucion iniciada a las {startedAtUtc:u}.");
    }

    public long Id { get; private set; }

    public string SourceFileName { get; private set; }

    public DateTime StartedAtUtc { get; private set; }

    public DateTime? FinishedAtUtc { get; private set; }

    public int TotalRecords { get; private set; }

    public int ImportedRecords { get; private set; }

    public int SkippedRecords { get; private set; }

    public int InconsistentRecords { get; private set; }

    public int FailedRecords { get; private set; }

    public ImportRunStatus Status { get; private set; }

    /// <summary>
    /// Incidencias retenidas (hasta <see cref="MaxIssues"/>), ordenadas por su secuencia de aparicion.
    /// </summary>
    public IReadOnlyList<string> Issues =>
        _issueEntries.OrderBy(entry => entry.Position).Select(entry => entry.Message).ToList();

    /// <summary>Entidades hijas de la bitacora; EF las persiste en la tabla <c>import_run_issues</c>.</summary>
    public ICollection<ImportRunIssue> IssueEntries => _issueEntries;

    public int DurationMilliseconds => FinishedAtUtc is null
        ? 0
        : (int)(FinishedAtUtc.Value - StartedAtUtc).TotalMilliseconds;

    public void CountRecord() => TotalRecords++;

    public void CountImported() => ImportedRecords++;

    public void CountSkipped(string reason)
    {
        SkippedRecords++;
        AddIssue(reason);
    }

    public void CountInconsistent(string reason)
    {
        InconsistentRecords++;
        AddIssue(reason);
    }

    public void CountFailed(string reason)
    {
        FailedRecords++;
        AddIssue(reason);
    }

    public void AddIssue(string reason)
    {
        // Se acota la bitacora para no degradar la respuesta del endpoint de importacion; la
        // secuencia global si avanza, de modo que las incidencias conservadas mantienen su orden real.
        if (_issueEntries.Count < MaxIssues)
        {
            _issueEntries.Add(new ImportRunIssue(_issueSequence, reason));
        }

        _issueSequence++;
    }

    public void Complete(DateTime finishedAtUtc)
    {
        FinishedAtUtc = finishedAtUtc;
        Status = FailedRecords > 0 || InconsistentRecords > 0
            ? ImportRunStatus.CompletedWithIssues
            : ImportRunStatus.Completed;
        AddIssue(
            $"Ejecucion finalizada: {ImportedRecords} importadas, {SkippedRecords} omitidas, " +
            $"{InconsistentRecords} inconsistentes, {FailedRecords} con error.");
        AddIssue($"Duracion: {DurationMilliseconds} ms.");
    }

    public void Fail(string reason, DateTime finishedAtUtc)
    {
        FinishedAtUtc = finishedAtUtc;
        Status = ImportRunStatus.Failed;
        AddIssue($"Ejecucion fallida: {reason}");
    }
}
