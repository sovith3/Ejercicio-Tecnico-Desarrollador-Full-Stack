namespace InvoiceHub.Domain.Enums;

/// <summary>Resultado de una ejecucion de importacion.</summary>
public enum ImportRunStatus
{
    Running = 0,
    Completed = 1,
    CompletedWithIssues = 2,
    Failed = 3
}
