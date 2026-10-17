namespace InvoiceHub.Application.Ports;

/// <summary>
/// Puerto de salida para el registro del pago de una factura. La validacion (factura existente, activa,
/// no cancelada y sin pago previo) se resuelve dentro de la misma sentencia SQL que la insercion.
/// </summary>
public interface IPaymentWritePort
{
    /// <summary>
    /// Registra el pago de la factura dentro de la base de datos, validando en la misma sentencia que
    /// la factura no se encuentre cancelada ni tenga ya un pago registrado.
    /// </summary>
    Task<PaymentAppendOutcome> RegisterPaymentAsync(
        PaymentAppendRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Datos necesarios para registrar un pago desde la base de datos.</summary>
public sealed record PaymentAppendRequest(
    long InvoiceId,
    string PaymentMethod,
    DateOnly PaymentDate,
    DateTime RecordedAtUtc);

/// <summary>Resultado de la insercion validada del pago.</summary>
public enum PaymentAppendOutcome
{
    Applied = 0,
    InvoiceNotFound = 1,
    InvoiceInconsistent = 2,
    InvoiceCancelled = 3,
    AlreadyPaid = 4
}
