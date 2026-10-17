using InvoiceHub.Domain.Entities;

namespace InvoiceHub.Application.Ports;

/// <summary>
/// Puerto de salida para la persistencia del agregado de factura (lado de escritura).
/// La implementacion concreta vive en la capa de infraestructura.
/// </summary>
public interface IInvoiceRepository
{
    /// <summary>Obtiene una factura con su grafo completo (cliente, detalle, notas de credito y pago).</summary>
    Task<Invoice?> GetByNumberAsync(int number, CancellationToken cancellationToken);

    Task<Invoice?> GetByIdAsync(long id, CancellationToken cancellationToken);

    Task<bool> ExistsByNumberAsync(int number, CancellationToken cancellationToken);

    /// <summary>Numero de nota de credito sugerido para una factura (maximo + 1 de todo el sistema).</summary>
    Task<int> GetNextCreditNoteNumberAsync(CancellationToken cancellationToken);

    Task AddAsync(Invoice invoice, CancellationToken cancellationToken);

    void Update(Invoice invoice);

    Task<int> CountAsync(bool onlyConsistent, CancellationToken cancellationToken);
}
