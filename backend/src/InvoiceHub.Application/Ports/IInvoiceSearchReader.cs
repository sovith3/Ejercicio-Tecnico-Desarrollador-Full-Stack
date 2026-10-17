using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.ReadModels;

namespace InvoiceHub.Application.Ports;

/// <summary>
/// Puerto de lectura optimizada de facturas. La implementacion consulta la vista
/// <c>vw_invoice_search</c>, que resuelve el join con cliente, notas de credito y pago, el saldo
/// pendiente y los dias de mora dentro del motor de base de datos.
/// </summary>
public interface IInvoiceSearchReader
{
    Task<PagedResult<InvoiceSearchRow>> SearchAsync(
        InvoiceSearchCriteria criteria,
        CancellationToken cancellationToken);

    Task<InvoiceSearchRow?> GetByNumberAsync(int number, CancellationToken cancellationToken);

    /// <summary>Conteo de facturas por cada estado, usado por los filtros de la interfaz.</summary>
    Task<IReadOnlyList<InvoiceStatusCountRow>> CountByInvoiceStatusAsync(
        bool onlyConsistent,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PaymentStatusCountRow>> CountByPaymentStatusAsync(
        bool onlyConsistent,
        CancellationToken cancellationToken);
}
