using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Entities;

namespace InvoiceHub.Application.Tests.Fakes;

/// <summary>Bitacora de importaciones falsa: conserva las ejecuciones registradas por el caso de uso.</summary>
internal sealed class FakeImportRunRepository : IImportRunRepository
{
    public List<ImportRun> Runs { get; } = new();

    public List<ImportRun> Added { get; } = new();

    public Task AddAsync(ImportRun importRun, CancellationToken cancellationToken)
    {
        Added.Add(importRun);
        Runs.Add(importRun);
        return Task.CompletedTask;
    }

    public Task<ImportRun?> GetLatestAsync(CancellationToken cancellationToken)
        => Task.FromResult(Runs.Count == 0 ? null : Runs[^1]);
}
