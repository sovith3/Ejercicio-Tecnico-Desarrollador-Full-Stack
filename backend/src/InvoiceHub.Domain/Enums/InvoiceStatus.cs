namespace InvoiceHub.Domain.Enums;

/// <summary>
/// Estado del documento de venta. Se deriva unicamente de las notas de credito asociadas,
/// nunca del valor declarado en el origen de datos.
/// </summary>
/// <remarks>
/// El valor numerico es parte del contrato con la base de datos: las rutinas SQL
/// (<c>sp_RecalculateInvoiceStatuses</c>, triggers de notas de credito) escriben estos mismos
/// ordinales. Modificar el orden implica actualizar los scripts de <c>database/scripts</c>.
/// </remarks>
public enum InvoiceStatus
{
    /// <summary>Factura emitida sin notas de credito.</summary>
    Issued = 0,

    /// <summary>Existen notas de credito cuyo monto acumulado es menor al total de la factura.</summary>
    Partial = 1,

    /// <summary>El monto acumulado de notas de credito iguala el total de la factura.</summary>
    Cancelled = 2
}
