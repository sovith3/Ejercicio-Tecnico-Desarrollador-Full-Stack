using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.Ports;
using InvoiceHub.Application.Tests.Fakes;
using InvoiceHub.Application.UseCases;
using Microsoft.Extensions.Logging.Abstractions;

namespace InvoiceHub.Application.Tests.UseCases;

/// <summary>
/// Pruebas del caso de uso de importacion. Se verifica el troceo del archivo en lotes, que cada desenlace
/// del motor se traduzca al contador de la bitacora (importada, omitida, inconsistente o fallida), que un
/// registro invalido no aborte la carga y que el recalculo set-based de estados se ejecute una sola vez al
/// cerrar la importacion.
/// </summary>
public sealed class ImportInvoicesHandlerTests
{
    private readonly FakeInvoiceSourceReader _source = new();
    private readonly FakeInvoiceRepository _invoices = new();
    private readonly FakeCustomerRepository _customers = new();
    private readonly FakeImportRunRepository _importRuns = new();
    private readonly FakeInvoiceImportPort _import = new();
    private readonly FakeStatusRecalculationPort _statuses = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly InMemoryCacheStore _cache = new();
    private readonly FixedClock _clock = FixedClock.Default();

    private ImportInvoicesHandler CreateHandler()
        => new(
            _source,
            _invoices,
            _customers,
            _importRuns,
            _import,
            _statuses,
            _unitOfWork,
            _cache,
            _clock,
            NullLogger<ImportInvoicesHandler>.Instance);

    /// <summary>
    /// Registro valido del origen. Las fechas son coherentes con <c>days_to_due</c> para que la carga no
    /// agregue avisos, y el detalle cuadra con el total declarado.
    /// </summary>
    private static SourceInvoice Source(
        int number,
        decimal total = 2000m,
        decimal unitPrice = 1000m,
        int quantity = 2,
        string customerRun = "12345678-9")
    {
        var invoiceDate = TestRows.Today.AddDays(-10);

        return new SourceInvoice
        {
            InvoiceNumber = number,
            InvoiceDate = invoiceDate,
            TotalAmount = total,
            DaysToDue = 30,
            PaymentDueDate = invoiceDate.AddDays(30),
            Details = new List<SourceInvoiceDetail>
            {
                new()
                {
                    ProductName = "Producto",
                    UnitPrice = unitPrice,
                    Quantity = quantity,
                    Subtotal = unitPrice * quantity
                }
            },
            Customer = new SourceCustomer
            {
                Run = customerRun,
                Name = $"Cliente {customerRun}",
                Email = $"{customerRun}@demo.cl"
            }
        };
    }

    [Fact]
    public async Task HandleAsync_WithMoreRecordsThanTheBatchSize_SendsConsecutiveBatchesAndRecalculatesOnce()
    {
        for (var offset = 1; offset <= 201; offset++)
        {
            _source.Invoices.Add(Source(number: 1000 + offset));
        }

        var summary = await CreateHandler().HandleAsync(CancellationToken.None);

        Assert.Equal(FakeInvoiceSourceReader.FileName, summary.SourceFile);
        Assert.Equal(201, summary.TotalRecords);
        Assert.Equal(201, summary.Imported);
        Assert.Equal(0, summary.Skipped);
        Assert.Equal(0, summary.Inconsistent);
        Assert.Equal(0, summary.Failed);

        // 200 registros en el primer lote y 1 en el segundo: la carga no espera el fin del archivo.
        Assert.Equal(2, _import.Batches.Count);
        Assert.Equal(200, _import.Batches[0].Count);
        Assert.Single(_import.Batches[1]);
        Assert.Equal(201, _import.Records.Count);

        // Un unico recalculo set-based, una confirmacion y una limpieza del rastreador al cerrar.
        Assert.Equal(1, _statuses.RecalculateAllCallCount);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
        Assert.Equal(1, _unitOfWork.ClearChangeTrackerCallCount);
        Assert.Single(_importRuns.Added);
    }

    [Fact]
    public async Task HandleAsync_WhenTheEngineReportsAnAlreadyPersistedNumber_CountsItAsSkipped()
    {
        _source.Invoices.Add(Source(number: 1000));
        _source.Invoices.Add(Source(number: 2000));

        _import.OutcomeSelector = record => record.Number == 1000
            ? new InvoiceImportOutcome(record.Number, InvoiceImportResult.AlreadyExists, 7)
            : new InvoiceImportOutcome(record.Number, InvoiceImportResult.Applied, 8);

        var summary = await CreateHandler().HandleAsync(CancellationToken.None);

        Assert.Equal(2, summary.TotalRecords);
        Assert.Equal(1, summary.Imported);
        Assert.Equal(1, summary.Skipped);
        Assert.Equal(0, summary.Failed);
        Assert.Contains(summary.Issues, issue => issue.Contains("1000") && issue.Contains("ya existe"));

        // La factura omitida no se recalcula: el motor ya la tenia persistida.
        Assert.Empty(_statuses.RecalculatedInvoiceIds);
    }

    [Fact]
    public async Task HandleAsync_WhenARecordIsInconsistent_CountsItAndKeepsGoing()
    {
        // Total declarado 2.000 con un detalle de 100: la factura se conserva fuera del sistema activo.
        _source.Invoices.Add(Source(number: 3000, total: 2000m, unitPrice: 100m, quantity: 1));
        _source.Invoices.Add(Source(number: 3001));

        var summary = await CreateHandler().HandleAsync(CancellationToken.None);

        Assert.Equal(2, summary.TotalRecords);
        Assert.Equal(2, summary.Imported);
        Assert.Equal(1, summary.Inconsistent);
        Assert.Equal(0, summary.Failed);
        Assert.Contains(
            summary.Issues,
            issue => issue.Contains("no coincide con la suma de subtotales") &&
                     issue.Replace(".", string.Empty).Replace(",", string.Empty).Contains("3000"));
    }

    [Fact]
    public async Task HandleAsync_WhenARecordIsInvalid_CountsItAsFailedAndContinuesWithTheRest()
    {
        _source.Invoices.Add(new SourceInvoice
        {
            InvoiceNumber = 4000,
            TotalAmount = 2000m,
            PaymentDueDate = TestRows.Today,
            Details = new List<SourceInvoiceDetail>
            {
                new() { ProductName = "Producto", UnitPrice = 2000m, Quantity = 1, Subtotal = 2000m }
            },
            Customer = new SourceCustomer { Run = "11111111-1", Name = "Cliente 1", Email = "c1@demo.cl" }
        });

        _source.Invoices.Add(Source(number: 4001));

        var summary = await CreateHandler().HandleAsync(CancellationToken.None);

        Assert.Equal(2, summary.TotalRecords);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, summary.Imported);
        Assert.Contains(summary.Issues, issue => issue.Contains("4000") && issue.Contains("fecha de emision"));

        // El registro valido si llego al motor: un registro invalido no aborta la carga.
        Assert.Single(_import.Batches);
    }

    [Fact]
    public async Task HandleAsync_WhenARecordHasNoInvoiceNumber_CountsItAsSkippedWithoutSendingItToTheEngine()
    {
        _source.Invoices.Add(new SourceInvoice
        {
            InvoiceDate = TestRows.Today.AddDays(-10),
            TotalAmount = 2000m,
            DaysToDue = 30,
            PaymentDueDate = TestRows.Today.AddDays(20),
            Details = new List<SourceInvoiceDetail>
            {
                new() { ProductName = "Producto", UnitPrice = 2000m, Quantity = 1, Subtotal = 2000m }
            },
            Customer = new SourceCustomer { Run = "22222222-2", Name = "Cliente 2", Email = "c2@demo.cl" }
        });

        _source.Invoices.Add(Source(number: 5001));

        var summary = await CreateHandler().HandleAsync(CancellationToken.None);
        var sentBatch = Assert.Single(_import.Batches);

        Assert.Equal(2, summary.TotalRecords);
        Assert.Equal(1, summary.Skipped);
        Assert.Equal(1, summary.Imported);
        Assert.Contains(summary.Issues, issue => issue.Contains("no informa numero de factura"));

        // El registro sin numero no se envia al motor: el lote lleva solo el registro valido.
        Assert.Single(sentBatch);
    }

    [Fact]
    public async Task HandleAsync_WhenTheEngineReturnsFewerOutcomesThanRecords_AbortsTheLoad()
    {
        _source.Invoices.Add(Source(number: 6001));
        _source.Invoices.Add(Source(number: 6002));

        _import.BatchOutcomes = _ => Array.Empty<InvoiceImportOutcome>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler().HandleAsync(CancellationToken.None));

        Assert.Contains("devolvio 0 resultado(s) para 2 registro(s) del lote", exception.Message);

        // El lote no es confiable: no se recalcula, no se registra la bitacora ni se invalida la cache.
        Assert.Equal(0, _statuses.RecalculateAllCallCount);
        Assert.Empty(_importRuns.Added);
        Assert.Empty(_cache.InvalidatedPrefixes);
    }
}
