using InvoiceHub.Application.Ports;
using Microsoft.Extensions.Logging;

namespace InvoiceHub.Infrastructure.Persistence.Routines;

/// <summary>
/// Adaptador del puerto <see cref="IStatusRecalculationPort"/>: ejecuta la rutina set-based que
/// recalcula consistencia, estado de factura y estado de cobro dentro del motor. La fecha de negocio
/// viaja como parametro para que la mora se evalue con el reloj de la aplicacion y no con
/// <c>date('now')</c> del motor.
/// </summary>
public sealed class StatusRecalculationRoutine : IStatusRecalculationPort
{
    private readonly IRoutineExecutor _executor;
    private readonly IClock _clock;
    private readonly ILogger<StatusRecalculationRoutine> _logger;

    public StatusRecalculationRoutine(
        IRoutineExecutor executor,
        IClock clock,
        ILogger<StatusRecalculationRoutine> logger)
    {
        _executor = executor;
        _clock = clock;
        _logger = logger;
    }

    public Task<int> RecalculateAllStatusesAsync(CancellationToken cancellationToken)
        => ExecuteAsync(null, cancellationToken);

    public Task<int> RecalculateInvoiceStatusesAsync(long invoiceId, CancellationToken cancellationToken)
        => ExecuteAsync(invoiceId, cancellationToken);

    /// <summary>
    /// Ejecuta el recalculo (factura unica o universo completo) y devuelve cuantas facturas fueron
    /// evaluadas. El guion SQL es el mismo: el parametro nulo significa "todas".
    /// </summary>
    private async Task<int> ExecuteAsync(long? invoiceId, CancellationToken cancellationToken)
    {
        var parameters = new
        {
            Today = SqliteValueFormat.ToStorage(_clock.Today),
            InvoiceId = invoiceId
        };

        var affected = await _executor
            .ExecuteAsync(SqlScriptCatalog.RecalculateStatuses, parameters, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogDebug(
            "Recalculo de estados aplicado a {Affected} factura(s) (Id: {InvoiceId}).",
            affected,
            invoiceId ?? 0);

        return affected;
    }
}
