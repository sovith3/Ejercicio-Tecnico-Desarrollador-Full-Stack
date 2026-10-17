using InvoiceHub.Domain.Common;

namespace InvoiceHub.Domain.Entities;

/// <summary>Linea de detalle de una factura.</summary>
public sealed class InvoiceDetail
{
    private InvoiceDetail()
    {
        ProductName = string.Empty;
    }

    public InvoiceDetail(string productName, decimal unitPrice, int quantity, decimal subtotal)
    {
        ProductName = Guard.NotEmpty(productName, nameof(productName));
        UnitPrice = Guard.NotNegative(unitPrice, nameof(unitPrice));
        Quantity = Guard.Positive(quantity, nameof(quantity));
        Subtotal = Guard.NotNegative(subtotal, nameof(subtotal));
    }

    public long Id { get; private set; }

    public long InvoiceId { get; private set; }

    public string ProductName { get; private set; } = string.Empty;

    public decimal UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    /// <summary>Subtotal con que viene la linea desde el origen de datos.</summary>
    /// <remarks>
    /// Se conserva tal cual llega desde el JSON (no se recalcula) porque precisamente ese valor es el que
    /// se compara contra <see cref="Invoice.TotalAmount"/> para determinar la consistencia.
    /// </remarks>
    public decimal Subtotal { get; private set; }

    public Invoice Invoice { get; private set; } = null!;

    /// <summary>Valor esperado de la linea segun precio unitario y cantidad.</summary>
    public decimal ExpectedSubtotal => UnitPrice * Quantity;

    public bool HasInconsistentSubtotal => ExpectedSubtotal != Subtotal;
}
