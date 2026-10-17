using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Entities;

namespace InvoiceHub.Application.Tests.Fakes;

/// <summary>
/// Catalogo de clientes falso: resuelve el indice por RUN con las mismas reglas que el adaptador EF Core
/// (comparacion sin distinguir mayusculas) y registra las altas solicitadas.
/// </summary>
internal sealed class FakeCustomerRepository : ICustomerRepository
{
    public List<Customer> Customers { get; } = new();

    public List<Customer> Added { get; } = new();

    public Task<Customer?> GetByRunAsync(string run, CancellationToken cancellationToken)
        => Task.FromResult(Customers.FirstOrDefault(customer =>
            string.Equals(customer.Run, run, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyDictionary<string, Customer>> GetByRunsAsync(
        IEnumerable<string> runs,
        CancellationToken cancellationToken)
    {
        var requested = new HashSet<string>(runs, StringComparer.OrdinalIgnoreCase);

        IReadOnlyDictionary<string, Customer> index = Customers
            .Where(customer => requested.Contains(customer.Run))
            .ToDictionary(customer => customer.Run, StringComparer.OrdinalIgnoreCase);

        return Task.FromResult(index);
    }

    public Task AddAsync(Customer customer, CancellationToken cancellationToken)
    {
        Added.Add(customer);
        Customers.Add(customer);
        return Task.CompletedTask;
    }

    public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(Customers.Count);
}
