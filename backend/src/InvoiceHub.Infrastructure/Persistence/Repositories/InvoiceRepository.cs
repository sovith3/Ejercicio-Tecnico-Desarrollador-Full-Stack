using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvoiceHub.Infrastructure.Persistence.Repositories;

/// <summary>
/// Adaptador de escritura del agregado de factura. La importacion agrega grafos completos (factura,
/// cliente, detalle, notas de credito y pago) y la API actualiza el estado derivado; en ambos casos las
/// reglas de negocio ya fueron validadas en el dominio, por lo que aqui solo se resuelve la persistencia.
/// </summary>
public sealed class InvoiceRepository : IInvoiceRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<InvoiceRepository> _logger;

    public InvoiceRepository(AppDbContext context, ILogger<InvoiceRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public Task<Invoice?> GetByNumberAsync(int number, CancellationToken cancellationToken)
        => QueryAggregate().FirstOrDefaultAsync(invoice => invoice.Number == number, cancellationToken);

    public Task<Invoice?> GetByIdAsync(long id, CancellationToken cancellationToken)
        => QueryAggregate().FirstOrDefaultAsync(invoice => invoice.Id == id, cancellationToken);

    public Task<bool> ExistsByNumberAsync(int number, CancellationToken cancellationToken)
        => _context.Invoices.AsNoTracking().AnyAsync(invoice => invoice.Number == number, cancellationToken);

    public async Task<int> GetNextCreditNoteNumberAsync(CancellationToken cancellationToken)
    {
        var maximum = await _context.CreditNotes
            .AsNoTracking()
            .Select(note => (int?)note.Number)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);

        return (maximum ?? 0) + 1;
    }

    public async Task AddAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        // El cliente puede venir del indice de la importacion (ya rastreado) o ser nuevo: en el segundo
        // caso EF debe insertarlo antes de la factura para obtener la clave foranea.
        AttachReference(invoice.Customer);

        await _context.Invoices.AddAsync(invoice, cancellationToken).ConfigureAwait(false);
    }

    public void Update(Invoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        _context.Invoices.Update(invoice);
        _logger.LogDebug("Factura {Number} marcada para actualizacion.", invoice.Number);
    }

    public Task<int> CountAsync(bool onlyConsistent, CancellationToken cancellationToken)
        => onlyConsistent
            ? _context.Invoices.AsNoTracking().CountAsync(invoice => invoice.IsConsistent, cancellationToken)
            : _context.Invoices.AsNoTracking().CountAsync(cancellationToken);

    private IQueryable<Invoice> QueryAggregate()
        => _context.Invoices
            .Include(invoice => invoice.Customer)
            .Include(invoice => invoice.Details)
            .Include(invoice => invoice.CreditNotes)
            .Include(invoice => invoice.Payment);

    /// <summary>
    /// Deja al cliente en el estado que corresponde a su origen: <c>Unchanged</c> si ya existe (lo
    /// devolvio el indice de la importacion) o <c>Added</c> si es nuevo. Actualizar una entidad ya
    /// rastreada es una operacion sin efecto, por eso el <c>Update</c> de EF no se usa aqui.
    /// </summary>
    private void AttachReference(Customer customer)
    {
        var entry = _context.Entry(customer);
        if (entry.State != EntityState.Detached)
        {
            return;
        }

        entry.State = customer.Id > 0 ? EntityState.Unchanged : EntityState.Added;
    }
}
