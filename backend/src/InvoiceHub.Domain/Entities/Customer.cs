using InvoiceHub.Domain.Common;

namespace InvoiceHub.Domain.Entities;

/// <summary>Cliente (receptor) de una o mas facturas.</summary>
public sealed class Customer
{
    private Customer()
    {
        Run = string.Empty;
        Name = string.Empty;
        Email = string.Empty;
    }

    public Customer(string run, string name, string email, DateTime createdAtUtc)
    {
        Run = Guard.NotEmpty(run, nameof(run));
        Name = Guard.NotEmpty(name, nameof(name));
        Email = Guard.NotEmpty(email, nameof(email));
        CreatedAtUtc = createdAtUtc;
    }

    public long Id { get; private set; }

    /// <summary>RUN/RUT del cliente. Identificador natural unico.</summary>
    public string Run { get; private set; }

    public string Name { get; private set; }

    public string Email { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public ICollection<Invoice> Invoices { get; private set; } = new List<Invoice>();

    public void UpdateContactData(string name, string email)
    {
        Name = Guard.NotEmpty(name, nameof(name));
        Email = Guard.NotEmpty(email, nameof(email));
    }
}
