using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Infrastructure.Persistence.Repositories;

/// <summary>
/// Adaptador del registro de ejecuciones de importacion. Cada carga deja su bitacora (contadores y
/// mensajes de incidencia); la ultima ejecucion alimenta el endpoint de estado del origen de datos que
/// usa la interfaz para decidir si es necesario importar.
/// </summary>
public sealed class ImportRunRepository : IImportRunRepository
{
    private readonly AppDbContext _context;

    public ImportRunRepository(AppDbContext context) => _context = context;

    public async Task AddAsync(ImportRun importRun, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(importRun);
        await _context.ImportRuns.AddAsync(importRun, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ultima ejecucion con sus incidencias. La bitacora se lee sin rastreo porque se proyecta
    /// directamente a la respuesta de la API (consultar dos veces el mismo historial no lo modifica).
    /// </summary>
    public Task<ImportRun?> GetLatestAsync(CancellationToken cancellationToken)
        => _context.ImportRuns
            .AsNoTracking()
            .Include(run => run.IssueEntries)
            .OrderByDescending(run => run.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
