namespace InvoiceHub.Domain.Entities;

/// <summary>
/// Incidencia registrada durante una ejecucion de importacion. Es una entidad hija de
/// <see cref="ImportRun"/>: se modela como tabla propia en lugar de serializar una lista dentro del
/// agregado, de modo que la bitacora pueda auditarse (ordenada por <see cref="Position"/>) sin
/// truncar los mensajes ni acoplar el esquema al formato de serializacion.
/// </summary>
public sealed class ImportRunIssue
{
    private ImportRunIssue()
    {
        Message = string.Empty;
    }

    internal ImportRunIssue(int position, string message)
    {
        Position = position;
        Message = message;
    }

    public long Id { get; private set; }

    public long ImportRunId { get; private set; }

    /// <summary>Orden de aparicion de la incidencia dentro de la ejecucion (secuencia global).</summary>
    public int Position { get; private set; }

    public string Message { get; private set; }

    public ImportRun ImportRun { get; private set; } = null!;
}
