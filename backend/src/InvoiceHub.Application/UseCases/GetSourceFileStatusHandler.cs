using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Entities;

namespace InvoiceHub.Application.UseCases;

/// <summary>
/// Caso de uso: estado del origen de datos JSON y de la ultima importacion. Es la consulta que usa la
/// interfaz para saber si hay que cargar el archivo o si ya fue importado.
/// </summary>
public sealed class GetSourceFileStatusHandler
{
    private readonly IInvoiceSourceReader _sourceReader;
    private readonly IInvoiceRepository _invoices;
    private readonly IImportRunRepository _importRuns;

    public GetSourceFileStatusHandler(
        IInvoiceSourceReader sourceReader,
        IInvoiceRepository invoices,
        IImportRunRepository importRuns)
    {
        _sourceReader = sourceReader;
        _invoices = invoices;
        _importRuns = importRuns;
    }

    public async Task<SourceFileStatusDto> HandleAsync(CancellationToken cancellationToken)
    {
        var descriptor = _sourceReader.Describe();
        var persistedInvoices = await _invoices.CountAsync(false, cancellationToken);
        var lastRun = await _importRuns.GetLatestAsync(cancellationToken);

        return new SourceFileStatusDto(
            descriptor.FilePath,
            descriptor.Exists,
            descriptor.SizeInBytes,
            descriptor.ModifiedAtUtc,
            persistedInvoices,
            lastRun is null ? null : ToDto(lastRun));
    }

    private static ImportRunDto ToDto(ImportRun run) => new(
        run.Id,
        run.SourceFileName,
        run.StartedAtUtc,
        run.FinishedAtUtc,
        run.TotalRecords,
        run.ImportedRecords,
        run.InconsistentRecords,
        run.SkippedRecords,
        run.FailedRecords,
        run.Status.ToString(),
        run.DurationMilliseconds,
        run.Issues);
}
