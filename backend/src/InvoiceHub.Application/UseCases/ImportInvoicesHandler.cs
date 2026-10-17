using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.Mapping;
using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Entities;
using InvoiceHub.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace InvoiceHub.Application.UseCases;

/// <summary>
/// Caso de uso: importar las facturas del archivo JSON de origen.
/// <para>
/// Reparto de la carga (ver README, seccion "Base de datos"): la aplicacion lee el archivo, valida cada
/// registro y construye el agregado; el motor de base de datos recibe el lote y resuelve de forma set-based
/// el alta de clientes, facturas, detalle, notas de credito y pago, la deteccion de numeros ya persistidos
/// y el recalculo final de estados. La aplicacion nunca trae el universo de facturas a memoria ni emite una
/// sentencia por registro.
/// </para>
/// <para>
/// Caracteristicas del proceso: es <b>idempotente</b> (el motor omite los numeros ya persistidos), es
/// <b>resiliente</b> (un registro invalido no aborta la carga: se contabiliza y se continua) y
/// <b>conserva</b> las facturas inconsistentes para que puedan reportarse, dejandolas fuera del sistema
/// activo. La carga se confirma por lotes para que las transacciones del motor sean cortas.
/// </para>
/// </summary>
public sealed class ImportInvoicesHandler
{
    /// <summary>Facturas por lote enviadas al motor antes de confirmar la transaccion.</summary>
    private const int BatchSize = 200;

    private readonly IInvoiceSourceReader _sourceReader;
    private readonly IInvoiceRepository _invoices;
    private readonly ICustomerRepository _customers;
    private readonly IImportRunRepository _importRuns;
    private readonly IInvoiceImportPort _importBatch;
    private readonly IStatusRecalculationPort _statuses;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheStore _cache;
    private readonly IClock _clock;
    private readonly ILogger<ImportInvoicesHandler> _logger;

    public ImportInvoicesHandler(
        IInvoiceSourceReader sourceReader,
        IInvoiceRepository invoices,
        ICustomerRepository customers,
        IImportRunRepository importRuns,
        IInvoiceImportPort importBatch,
        IStatusRecalculationPort statuses,
        IUnitOfWork unitOfWork,
        ICacheStore cache,
        IClock clock,
        ILogger<ImportInvoicesHandler> logger)
    {
        _sourceReader = sourceReader;
        _invoices = invoices;
        _customers = customers;
        _importRuns = importRuns;
        _importBatch = importBatch;
        _statuses = statuses;
        _unitOfWork = unitOfWork;
        _cache = cache;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ImportSummaryDto> HandleAsync(CancellationToken cancellationToken)
    {
        var document = await _sourceReader.ReadAsync(cancellationToken);
        var startedAt = _clock.UtcNow;
        var today = _clock.Today;

        var run = new ImportRun(document.FileName, startedAt);

        // El indice de clientes se resuelve con una unica consulta (no una por factura) y el catalogo
        // definitivo lo materializa la rutina de carga dentro del motor de base de datos.
        var customerIndex = await BuildCustomerIndexAsync(document, cancellationToken);
        var creditNoteSequencer = new CreditNoteNumberSequencer(
            await _invoices.GetNextCreditNoteNumberAsync(cancellationToken));
        var usedCreditNoteNumbers = new HashSet<int>();

        // Lote en memoria: se entrega al motor y se vacia en cada confirmacion.
        var batch = new List<InvoicePersistRecord>(BatchSize);

        foreach (var source in document.Invoices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            run.CountRecord();

            try
            {
                if (source.InvoiceNumber is null)
                {
                    run.CountSkipped($"Registro {run.TotalRecords}: no informa numero de factura.");
                    continue;
                }

                // El numero ya persistido NO se consulta aqui: lo decide la rutina de carga dentro del
                // motor (idempotencia set-based) y su desenlace se contabiliza al confirmar el lote.
                var number = source.InvoiceNumber.Value;

                var (customer, customerNotice) = ResolveCustomer(source.Customer, customerIndex, _clock.UtcNow);
                if (customerNotice is not null)
                {
                    run.AddIssue(customerNotice);
                }

                var build = SourceInvoiceFactory.Build(
                    source,
                    customer,
                    today,
                    _clock.UtcNow,
                    () => creditNoteSequencer.Reserve(number, usedCreditNoteNumbers, run));

                foreach (var notice in build.Notices)
                {
                    run.AddIssue(notice);
                }

                batch.Add(InvoicePersistMapper.ToRecord(build.Invoice));

                if (batch.Count >= BatchSize)
                {
                    await FlushBatchAsync(batch, run, cancellationToken);
                }
            }
            catch (DomainException exception)
            {
                run.CountFailed($"Factura {source.InvoiceNumber?.ToString() ?? "?"}: {exception.Message}");
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Error inesperado al importar la factura {Number}.", source.InvoiceNumber);
                run.CountFailed(
                    $"Factura {source.InvoiceNumber?.ToString() ?? "?"}: error inesperado ({exception.Message}).");
            }
        }

        if (batch.Count > 0)
        {
            await FlushBatchAsync(batch, run, cancellationToken);
        }

        // Recalculo masivo (set-based) dentro del motor: consistencia, estado de factura y estado de
        // cobro de todas las filas, en una sola pasada.
        await _statuses.RecalculateAllStatusesAsync(cancellationToken);

        run.Complete(_clock.UtcNow);
        await _importRuns.AddAsync(run, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _unitOfWork.ClearChangeTracker();
        await _cache.InvalidateInvoiceReadModelsAsync(cancellationToken);

        return new ImportSummaryDto(
            document.FileName,
            run.StartedAtUtc,
            run.FinishedAtUtc ?? _clock.UtcNow,
            run.TotalRecords,
            run.ImportedRecords,
            run.InconsistentRecords,
            run.SkippedRecords,
            run.FailedRecords,
            run.DurationMilliseconds,
            run.Status.ToString(),
            run.Issues);
    }

    private static (Customer Customer, string? Notice) ResolveCustomer(
        SourceCustomer? source,
        IDictionary<string, Customer> index,
        DateTime nowUtc)
    {
        if (source is null)
        {
            throw new InvariantViolationException("La factura no informa los datos del cliente.");
        }

        var run = source.Run?.Trim();
        if (string.IsNullOrEmpty(run))
        {
            throw new InvariantViolationException("El cliente no informa RUN.");
        }

        var name = source.Name?.Trim();
        var email = source.Email?.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(email))
        {
            throw new InvariantViolationException($"El cliente {run} no informa nombre y/o correo de contacto.");
        }

        if (index.TryGetValue(run, out var existing))
        {
            return existing.Name == name && existing.Email == email
                ? (existing, null)
                : (existing, $"Cliente {run}: el origen repite el RUN con nombre o correo distintos; se conservan los datos del primer registro.");
        }

        var created = new Customer(run, name, email, nowUtc);
        index[run] = created;
        return (created, null);
    }

    private async Task<Dictionary<string, Customer>> BuildCustomerIndexAsync(
        InvoiceSourceDocument document,
        CancellationToken cancellationToken)
    {
        var runs = document.Invoices
            .Select(invoice => invoice.Customer?.Run?.Trim())
            .Where(run => !string.IsNullOrEmpty(run))
            .Select(run => run!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var persisted = await _customers.GetByRunsAsync(runs, cancellationToken);
        return new Dictionary<string, Customer>(persisted, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Entrega el lote al motor de base de datos y traduce cada desenlace al contador de la bitacora.
    /// La rutina decide si el numero ya existia (carga idempotente) y aplica el resto del grafo de la
    /// factura en una sola transaccion: aqui no se emite ninguna sentencia por registro.
    /// </summary>
    private async Task FlushBatchAsync(
        List<InvoicePersistRecord> batch,
        ImportRun run,
        CancellationToken cancellationToken)
    {
        var outcomes = await _importBatch.ImportBatchAsync(batch, cancellationToken);

        if (outcomes.Count != batch.Count)
        {
            throw new InvalidOperationException(
                $"La rutina de carga devolvio {outcomes.Count} resultado(s) para {batch.Count} registro(s) del lote.");
        }

        for (var index = 0; index < batch.Count; index++)
        {
            var record = batch[index];

            if (outcomes[index].Result == InvoiceImportResult.AlreadyExists)
            {
                run.CountSkipped($"Factura {record.Number}: ya existe en el sistema, se omite (carga idempotente).");
                continue;
            }

            run.CountImported();

            if (!record.IsConsistent)
            {
                run.CountInconsistent($"Factura {record.Number}: {record.InconsistencyReason}");
            }
        }

        batch.Clear();
    }

    /// <summary>Entrega numeros correlativos de notas de credito cuando el origen no informa uno utilizable.</summary>
    private sealed class CreditNoteNumberSequencer
    {
        private int _current;

        public CreditNoteNumberSequencer(int startingNumber) => _current = startingNumber;

        public int Reserve(int invoiceNumber, ISet<int> alreadyUsed, ImportRun run)
        {
            int candidate;
            do
            {
                candidate = _current++;
            }
            while (!alreadyUsed.Add(candidate));

            run.AddIssue(
                $"Factura {invoiceNumber}: la nota de credito no informaba un numero valido; se asigno el numero {candidate}.");
            return candidate;
        }
    }
}
