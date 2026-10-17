namespace InvoiceHub.Application.Ports;

/// <summary>
/// Puerto de salida para el recalculo de los tres valores derivados de una factura (consistencia de
/// montos, estado de factura y estado de cobro). La operacion se resuelve de forma set-based *dentro*
/// del motor de base de datos: la aplicacion no trae filas a memoria ni recorre factura por factura.
/// </summary>
/// <remarks>
/// Se declara como puerto independiente (en lugar de agruparse con las rutinas de escritura) para que
/// cada caso de uso dependa unicamente de la operacion que realmente necesita.
/// </remarks>
public interface IStatusRecalculationPort
{
    /// <summary>
    /// Recalcula de forma conjunta (set-based) consistencia, estado de factura y estado de cobro de
    /// todas las facturas. Devuelve la cantidad de facturas evaluadas.
    /// </summary>
    Task<int> RecalculateAllStatusesAsync(CancellationToken cancellationToken);

    /// <summary>Recalcula consistencia, estado de factura y estado de cobro de una unica factura.</summary>
    Task<int> RecalculateInvoiceStatusesAsync(long invoiceId, CancellationToken cancellationToken);
}
