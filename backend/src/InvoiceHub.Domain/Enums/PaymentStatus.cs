namespace InvoiceHub.Domain.Enums;

/// <summary>
/// Estado de cobro de una factura. Se recalcula en cada evaluacion a partir del pago registrado
/// y de la fecha de vencimiento pactada.
/// </summary>
/// <remarks>
/// Los ordinales son el contrato con las rutinas SQL que recalculan el estado de cobro.
/// </remarks>
public enum PaymentStatus
{
    /// <summary>Sin pago registrado y aun dentro del plazo pactado.</summary>
    Pending = 0,

    /// <summary>Sin pago registrado y con la fecha de vencimiento superada.</summary>
    Overdue = 1,

    /// <summary>Existe un pago registrado para la factura.</summary>
    Paid = 2
}
