using System.Text.Json.Serialization;

namespace InvoiceHub.Application.Ports;

/// <summary>
/// Modelo de lectura del archivo JSON de origen (contrato del adaptador de entrada de datos).
/// Se declara en la capa de aplicacion para que el adaptador de archivo dependa de la abstraccion.
/// </summary>
public sealed class SourceInvoiceFile
{
    [JsonPropertyName("invoices")]
    public List<SourceInvoice> Invoices { get; init; } = new();
}

public sealed class SourceInvoice
{
    [JsonPropertyName("invoice_number")]
    public int? InvoiceNumber { get; init; }

    [JsonPropertyName("invoice_date")]
    public DateOnly? InvoiceDate { get; init; }

    [JsonPropertyName("invoice_status")]
    public string? DeclaredStatus { get; init; }

    [JsonPropertyName("total_amount")]
    public decimal? TotalAmount { get; init; }

    [JsonPropertyName("days_to_due")]
    public int? DaysToDue { get; init; }

    [JsonPropertyName("payment_due_date")]
    public DateOnly? PaymentDueDate { get; init; }

    [JsonPropertyName("payment_status")]
    public string? DeclaredPaymentStatus { get; init; }

    [JsonPropertyName("invoice_detail")]
    public List<SourceInvoiceDetail> Details { get; init; } = new();

    [JsonPropertyName("invoice_payment")]
    public SourcePayment? Payment { get; init; }

    [JsonPropertyName("invoice_credit_note")]
    public List<SourceCreditNote> CreditNotes { get; init; } = new();

    [JsonPropertyName("customer")]
    public SourceCustomer? Customer { get; init; }
}

public sealed class SourceInvoiceDetail
{
    [JsonPropertyName("product_name")]
    public string? ProductName { get; init; }

    [JsonPropertyName("unit_price")]
    public decimal? UnitPrice { get; init; }

    [JsonPropertyName("quantity")]
    public int? Quantity { get; init; }

    [JsonPropertyName("subtotal")]
    public decimal? Subtotal { get; init; }
}

public sealed class SourceCreditNote
{
    [JsonPropertyName("credit_note_number")]
    public int? CreditNoteNumber { get; init; }

    [JsonPropertyName("credit_note_date")]
    public DateOnly? CreditNoteDate { get; init; }

    [JsonPropertyName("credit_note_amount")]
    public decimal? CreditNoteAmount { get; init; }
}

public sealed class SourcePayment
{
    [JsonPropertyName("payment_method")]
    public string? PaymentMethod { get; init; }

    [JsonPropertyName("payment_date")]
    public DateOnly? PaymentDate { get; init; }
}

public sealed class SourceCustomer
{
    [JsonPropertyName("customer_run")]
    public string? Run { get; init; }

    [JsonPropertyName("customer_name")]
    public string? Name { get; init; }

    [JsonPropertyName("customer_email")]
    public string? Email { get; init; }
}

/// <summary>Documento leido desde el origen de datos.</summary>
public sealed record InvoiceSourceDocument(string FilePath, IReadOnlyList<SourceInvoice> Invoices)
{
    public string FileName => Path.GetFileName(FilePath);
}

/// <summary>Metadatos del archivo de origen, usados por el endpoint de diagnostico.</summary>
public sealed record SourceFileDescriptor(string FilePath, bool Exists, long SizeInBytes, DateTime? ModifiedAtUtc);

/// <summary>
/// Puerto de entrada de datos: lee las facturas desde el archivo JSON informado en configuracion.
/// </summary>
public interface IInvoiceSourceReader
{
    Task<InvoiceSourceDocument> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Describe el archivo configurado sin leer su contenido.</summary>
    SourceFileDescriptor Describe();
}
