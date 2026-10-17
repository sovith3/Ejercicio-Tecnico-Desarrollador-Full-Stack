using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Infrastructure.Persistence.Repositories;

/// <summary>
/// Adaptador del catalogo de clientes. Durante la importacion del archivo se resuelven los RUN en una
/// sola consulta por lote (y no una por factura), que es la diferencia entre un proceso lineal y uno
/// cuadratico con el volumen de facturas.
/// </summary>
public sealed class CustomerRepository : ICustomerRepository
{
    /// <summary>SQLite limita la cantidad de parametros por sentencia; se consulta por lotes.</summary>
    private const int ChunkSize = 500;

    private readonly AppDbContext _context;

    public CustomerRepository(AppDbContext context) => _context = context;

    public Task<Customer?> GetByRunAsync(string run, CancellationToken cancellationToken)
        => _context.Customers.FirstOrDefaultAsync(customer => customer.Run == run, cancellationToken);

    public async Task<IReadOnlyDictionary<string, Customer>> GetByRunsAsync(
        IEnumerable<string> runs,
        CancellationToken cancellationToken)
    {
        var normalized = runs
            .Where(run => !string.IsNullOrWhiteSpace(run))
            .Select(run => run.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var index = new Dictionary<string, Customer>(StringComparer.OrdinalIgnoreCase);
        if (normalized.Count == 0)
        {
            return index;
        }

        foreach (var chunk in normalized.Chunk(ChunkSize))
        {
            // Se retornan entidades rastreadas: la importacion las reutiliza para no duplicar clientes.
            var found = await _context.Customers
                .Where(customer => chunk.Contains(customer.Run))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var customer in found)
            {
                index[customer.Run] = customer;
            }
        }

        return index;
    }

    public async Task AddAsync(Customer customer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(customer);
        await _context.Customers.AddAsync(customer, cancellationToken).ConfigureAwait(false);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken)
        => _context.Customers.AsNoTracking().CountAsync(cancellationToken);
}
