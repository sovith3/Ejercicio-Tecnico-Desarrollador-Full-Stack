using InvoiceHub.Application.Ports;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceHub.Infrastructure.Tests.SourceData;

/// <summary>
/// Pruebas del lector del archivo JSON del enunciado. Se verifica la deserializacion tolerante (comentarios,
/// comas finales y numeros como cadena), la resolucion del archivo configurado y el error explicito cuando
/// el origen no existe.
/// </summary>
public sealed class InvoiceSourceReaderTests : IAsyncLifetime
{
    private const int SourceInvoiceCount = 50;
    private const int SourceCreditNoteCount = 7;
    private const int SourcePaymentCount = 14;

    private readonly TestHost _host = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    private Task<T> WithReaderAsync<T>(Func<IInvoiceSourceReader, Task<T>> action)
        => _host.WithScopeAsync(provider => action(provider.GetRequiredService<IInvoiceSourceReader>()));

    [Fact]
    public void Describe_ReportsTheConfiguredFileWithItsMetadata()
    {
        var descriptor = _host.Provider.GetRequiredService<IInvoiceSourceReader>().Describe();

        Assert.True(descriptor.Exists);
        Assert.Equal("bd_exam_invoices.json", Path.GetFileName(descriptor.FilePath));
        Assert.Equal(new FileInfo(TestHost.SourceFilePath).Length, descriptor.SizeInBytes);
        Assert.NotNull(descriptor.ModifiedAtUtc);
    }

    [Fact]
    public async Task ReadAsync_ReadsEveryInvoiceWithCustomerAndDetails()
    {
        var document = await WithReaderAsync(reader => reader.ReadAsync(CancellationToken.None));

        Assert.Equal("bd_exam_invoices.json", document.FileName);
        Assert.Equal(SourceInvoiceCount, document.Invoices.Count);
        Assert.All(document.Invoices, invoice => Assert.NotNull(invoice.Customer));

        var first = document.Invoices[0];
        Assert.Equal(1, first.InvoiceNumber);
        Assert.Equal(new DateOnly(2025, 1, 14), first.InvoiceDate);
        Assert.Equal(265019m, first.TotalAmount);
        Assert.Equal(75, first.DaysToDue);
        Assert.Equal(new DateOnly(2025, 7, 31), first.PaymentDueDate);
        Assert.Equal("issued", first.DeclaredStatus);
        Assert.Equal("Pending", first.DeclaredPaymentStatus);
        Assert.Equal("13075795-2", first.Customer!.Run);
        Assert.Equal("Juanita Hugh", first.Customer.Name);

        var detail = Assert.Single(first.Details);
        Assert.Equal(3193m, detail.UnitPrice);
        Assert.Equal(83, detail.Quantity);
        Assert.Equal(265019m, detail.Subtotal);
    }

    [Fact]
    public async Task ReadAsync_ReadsTheCreditNotesAndPaymentsPresentInTheSource()
    {
        var document = await WithReaderAsync(reader => reader.ReadAsync(CancellationToken.None));

        var creditNotes = document.Invoices.SelectMany(invoice => invoice.CreditNotes).ToList();
        var paidInvoices = document.Invoices
            .Count(invoice => !string.IsNullOrWhiteSpace(invoice.Payment?.PaymentMethod));

        Assert.Equal(SourceCreditNoteCount, creditNotes.Count);
        Assert.Equal(SourcePaymentCount, paidInvoices);
        Assert.All(creditNotes, note => Assert.True(note.CreditNoteAmount > 0m));
    }

    [Fact]
    public async Task ReadAsync_AcceptsCommentsTrailingCommasAndNumericStrings()
    {
        var path = Path.Combine(_host.RootPath, "origen-tolerante.json");
        await File.WriteAllTextAsync(path, """
            {
              // Comentario no estandar: el lector debe ignorarlo.
              "invoices": [
                {
                  "invoice_number": "70001",
                  "invoice_date": "2025-03-01",
                  "total_amount": "1190.00",
                  "days_to_due": "30",
                  "payment_due_date": "2025-03-31",
                  "invoice_detail": [
                    { "product_name": "Servicio", "unit_price": "595", "quantity": "2", "subtotal": "1190" }
                  ],
                  "customer": { "customer_run": "11111111-1", "customer_name": "Cliente Uno", "customer_email": "uno@demo.cl" }
                },
              ],
            }
            """);

        using var host = new TestHost(overrides: new Dictionary<string, string?>
        {
            ["SourceData:FilePath"] = path
        });

        var document = await host.WithScopeAsync(provider =>
            provider.GetRequiredService<IInvoiceSourceReader>().ReadAsync(CancellationToken.None));

        var invoice = Assert.Single(document.Invoices);
        Assert.Equal(70001, invoice.InvoiceNumber);
        Assert.Equal(1190m, invoice.TotalAmount);
        Assert.Equal(30, invoice.DaysToDue);
        Assert.Equal(2, invoice.Details.Single().Quantity);
        Assert.Equal("Cliente Uno", invoice.Customer!.Name);
    }

    [Fact]
    public async Task ReadAsync_WhenTheFileDoesNotExist_ThrowsFileNotFoundWithTheExpectedPath()
    {
        using var host = new TestHost(overrides: new Dictionary<string, string?>
        {
            ["SourceData:FilePath"] = Path.Combine(Path.GetTempPath(), "invoicehub-origen-inexistente.json")
        });

        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() => host.WithScopeAsync(provider =>
            provider.GetRequiredService<IInvoiceSourceReader>().ReadAsync(CancellationToken.None)));

        Assert.Contains("no se encontro el archivo json", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invoicehub-origen-inexistente.json", exception.Message);

        var descriptor = host.Provider.GetRequiredService<IInvoiceSourceReader>().Describe();
        Assert.False(descriptor.Exists);
        Assert.Equal(0, descriptor.SizeInBytes);
        Assert.Null(descriptor.ModifiedAtUtc);
    }

    [Fact]
    public async Task ReadAsync_WhenTheFileExceedsTheConfiguredLimit_ThrowsInvalidData()
    {
        using var host = new TestHost(overrides: new Dictionary<string, string?>
        {
            ["SourceData:FilePath"] = TestHost.SourceFilePath,
            ["SourceData:MaxFileSizeBytes"] = "1024"
        });

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => host.WithScopeAsync(provider =>
            provider.GetRequiredService<IInvoiceSourceReader>().ReadAsync(CancellationToken.None)));

        Assert.Contains("supera el", exception.Message);
        Assert.Contains("SourceData:MaxFileSizeBytes", exception.Message);
    }
}
