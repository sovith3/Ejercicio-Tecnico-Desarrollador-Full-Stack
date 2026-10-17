using InvoiceHub.Domain.Enums;

namespace InvoiceHub.Application.Ports;

/// <summary>
/// Puerto de salida de la carga masiva. La capa de aplicacion valida y construye el agregado; el
/// adaptador entrega el lote al motor de base de datos, donde una rutina set-based (staging + merge)
/// resuelve de una sola vez el alta de clientes, facturas, detalle, notas de credito y pago, ademas de
/// la deteccion de numeros ya persistidos. Asi el volumen de la carga no se convierte en una ida y
/// vuelta por registro.
/// </summary>
public interface IInvoiceImportPort
{
    /// <summary>
    /// Persiste un lote de facturas ya validado por el dominio. Devuelve un resultado por cada registro
    /// recibido, en el mismo orden, indicando si fue aplicado o si su numero ya existia (carga
    /// idempotente).
    /// </summary>
    Task<IReadOnlyList<InvoiceImportOutcome>> ImportBatchAsync(
        IReadOnlyList<InvoicePersistRecord> records,
        CancellationToken cancellationToken);
}

/// <summary>
/// Factura lista para persistir: contiene los valores ya calculados por el dominio (montos en unidades
/// enteras de 1/100000 de peso, ordinales de los enums y fechas de negocio) para que el motor solo tenga
/// que insertar filas.
/// </summary>
public sealed record InvoicePersistRecord(
    int Number,
    DateOnly InvoiceDate,
    long TotalAmountUnits,
    int DaysToDue,
    DateOnly PaymentDueDate,
    InvoiceStatus Status,
    PaymentStatus PaymentStatus,
    bool IsConsistent,
    string? InconsistencyReason,
    InvoiceStatus? DeclaredStatus,
    PaymentStatus? DeclaredPaymentStatus,
    string CustomerRun,
    string CustomerName,
    string CustomerEmail,
    IReadOnlyList<InvoicePersistDetail> Details,
    IReadOnlyList<InvoicePersistCreditNote> CreditNotes,
    InvoicePersistPayment? Payment,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>Linea de detalle de una factura a persistir (montos en unidades de 1/100000 de peso).</summary>
public sealed record InvoicePersistDetail(
    string ProductName,
    long UnitPriceUnits,
    int Quantity,
    long SubtotalUnits);

/// <summary>Nota de credito de una factura a persistir (monto en unidades de 1/100000 de peso).</summary>
public sealed record InvoicePersistCreditNote(
    int Number,
    DateOnly CreditNoteDate,
    long AmountUnits,
    DateTime CreatedAtUtc);

/// <summary>Pago informado por el origen para una factura a persistir.</summary>
public sealed record InvoicePersistPayment(
    string PaymentMethod,
    DateOnly PaymentDate,
    DateTime RecordedAtUtc);

/// <summary>Resultado de persistir un registro del lote.</summary>
public sealed record InvoiceImportOutcome(int Number, InvoiceImportResult Result, long? InvoiceId);

/// <summary>Desenlace de la persistencia de un registro del lote (ordinales compartidos con la rutina SQL).</summary>
public enum InvoiceImportResult
{
    /// <summary>La factura y su grafo se insertaron en esta ejecucion.</summary>
    Applied = 0,

    /// <summary>El numero ya existia en el sistema (o se repitio dentro del lote): no se duplica.</summary>
    AlreadyExists = 1
}
