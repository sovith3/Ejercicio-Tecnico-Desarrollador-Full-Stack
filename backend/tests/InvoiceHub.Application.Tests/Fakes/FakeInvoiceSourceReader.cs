using InvoiceHub.Application.Ports;

namespace InvoiceHub.Application.Tests.Fakes;

/// <summary>
/// Lector del archivo de origen falso: entrega el documento armado por la prueba, de modo que el caso de
/// uso de importacion se ejerza sin tocar el disco.
/// </summary>
internal sealed class FakeInvoiceSourceReader : IInvoiceSourceReader
{
    public const string FileName = "origen-pruebas.json";

    public List<SourceInvoice> Invoices { get; } = new();

    public Task<InvoiceSourceDocument> ReadAsync(CancellationToken cancellationToken)
        => Task.FromResult(new InvoiceSourceDocument(FileName, Invoices.ToList()));

    public SourceFileDescriptor Describe()
        => new(FileName, Exists: true, SizeInBytes: 0, ModifiedAtUtc: null);
}
