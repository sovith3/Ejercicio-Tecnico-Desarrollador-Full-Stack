using InvoiceHub.Application.Ports;

namespace InvoiceHub.Application.Tests.Fakes;

/// <summary>
/// Puerto de carga masiva falso. Conserva los lotes recibidos y permite decidir el desenlace de cada
/// registro, de modo que las pruebas del caso de uso comprueben el troceo en lotes, la contabilizacion
/// de la bitacora y el manejo de duplicados sin cargar una base real.
/// </summary>
internal sealed class FakeInvoiceImportPort : IInvoiceImportPort
{
    /// <summary>Desenlace por registro; si no se informa, todos se consideran aplicados.</summary>
    public Func<InvoicePersistRecord, InvoiceImportOutcome>? OutcomeSelector { get; set; }

    /// <summary>
    /// Desenlaces del lote completo; permite reproducir una respuesta inconsistente del motor (por ejemplo
    /// menos resultados que registros) sin cambiar el contrato del puerto.
    /// </summary>
    public Func<IReadOnlyList<InvoicePersistRecord>, IReadOnlyList<InvoiceImportOutcome>>? BatchOutcomes { get; set; }

    /// <summary>Registros recibidos, en orden, a traves de todos los lotes.</summary>
    public List<InvoicePersistRecord> Records { get; } = new();

    /// <summary>Lotes recibidos por invocacion (permite verificar el tamano del troceo).</summary>
    public List<IReadOnlyList<InvoicePersistRecord>> Batches { get; } = new();

    public Task<IReadOnlyList<InvoiceImportOutcome>> ImportBatchAsync(
        IReadOnlyList<InvoicePersistRecord> records,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);

        Batches.Add(records.ToList());
        Records.AddRange(records);

        IReadOnlyList<InvoiceImportOutcome> outcomes = BatchOutcomes?.Invoke(records)
            ?? records
                .Select(record => OutcomeSelector?.Invoke(record)
                                  ?? new InvoiceImportOutcome(record.Number, InvoiceImportResult.Applied, null))
                .ToList();

        return Task.FromResult(outcomes);
    }
}
