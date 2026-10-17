namespace InvoiceHub.Application.Ports;

/// <summary>
/// Puerto de salida para las operaciones de nota de credito que resuelve el motor de base de datos:
/// la validacion atomica del saldo pendiente y su insercion (en la misma sentencia SQL) y el saldo
/// vigente calculado por el propio motor (en unidades enteras de 1/100000 de peso, para no exponer
/// aritmetica de punto flotante).
/// </summary>
public interface ICreditNoteWritePort
{
    /// <summary>
    /// Inserta una nota de credito validando de forma atomica, dentro de la propia sentencia SQL, que
    /// el monto no supere el saldo pendiente de la factura. Evita condiciones de carrera entre
    /// solicitudes concurrentes.
    /// </summary>
    Task<CreditNoteAppendOutcome> AppendCreditNoteAsync(
        CreditNoteAppendRequest request,
        CancellationToken cancellationToken);

    /// <summary>Saldo pendiente calculado por el motor (unidades enteras de 1/100000 de peso).</summary>
    Task<long?> GetOutstandingBalanceUnitsAsync(long invoiceId, CancellationToken cancellationToken);
}

/// <summary>Datos necesarios para registrar una nota de credito desde la base de datos.</summary>
public sealed record CreditNoteAppendRequest(
    long InvoiceId,
    int Number,
    DateOnly CreditNoteDate,
    long AmountUnits,
    DateTime CreatedAtUtc);

/// <summary>Resultado de la insercion validada de una nota de credito.</summary>
public enum CreditNoteAppendOutcome
{
    Applied = 0,
    InvoiceNotFound = 1,
    InvoiceInconsistent = 2,
    InvoiceCancelled = 3,
    ExceedsOutstandingBalance = 4,
    DuplicatedNumber = 5
}
