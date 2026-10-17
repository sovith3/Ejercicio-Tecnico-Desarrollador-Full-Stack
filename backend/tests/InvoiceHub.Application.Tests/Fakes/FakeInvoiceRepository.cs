using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Entities;

namespace InvoiceHub.Application.Tests.Fakes;

/// <summary>
/// Repositorio de facturas falso con los mismos invariantes que el adaptador EF Core: expone los
/// agregados por numero, cuenta el universo consistente y registra las escrituras solicitadas.
/// </summary>
internal sealed class FakeInvoiceRepository : IInvoiceRepository
{
    public List<Invoice> Invoices { get; } = new();

    public List<Invoice> Added { get; } = new();

    public List<Invoice> Updated { get; } = new();

    public int NextCreditNoteNumber { get; set; } = 9001;

    public int GetByNumberCallCount { get; private set; }

    public bool? LastCountOnlyConsistent { get; private set; }

    public Task<Invoice?> GetByNumberAsync(int number, CancellationToken cancellationToken)
    {
        GetByNumberCallCount++;
        return Task.FromResult(Invoices.FirstOrDefault(invoice => invoice.Number == number));
    }

    public Task<Invoice?> GetByIdAsync(long id, CancellationToken cancellationToken)
        => Task.FromResult(Invoices.FirstOrDefault(invoice => invoice.Id == id));

    public Task<bool> ExistsByNumberAsync(int number, CancellationToken cancellationToken)
        => Task.FromResult(Invoices.Any(invoice => invoice.Number == number));

    public Task<int> GetNextCreditNoteNumberAsync(CancellationToken cancellationToken)
        => Task.FromResult(NextCreditNoteNumber);

    public Task AddAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        Added.Add(invoice);
        Invoices.Add(invoice);
        return Task.CompletedTask;
    }

    public void Update(Invoice invoice) => Updated.Add(invoice);

    public Task<int> CountAsync(bool onlyConsistent, CancellationToken cancellationToken)
    {
        LastCountOnlyConsistent = onlyConsistent;
        return Task.FromResult(onlyConsistent
            ? Invoices.Count(invoice => invoice.IsConsistent)
            : Invoices.Count);
    }
}
