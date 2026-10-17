using InvoiceHub.Application.Ports;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace InvoiceHub.Infrastructure.Persistence.Routines;

/// <summary>
/// Adaptador del puerto <see cref="ICreditNoteWritePort"/>. La validacion del saldo y la insercion
/// ocurren en la misma sentencia SQL, dentro de una transaccion inmediata, de modo que dos solicitudes
/// concurrentes no puedan superar el saldo pendiente de la factura.
/// </summary>
public sealed class CreditNoteWriteRoutine : ICreditNoteWritePort
{
    /// <summary>Codigo de error de SQLite para violaciones de restriccion (indices unicos).</summary>
    private const int ConstraintViolation = 19;

    private readonly IRoutineExecutor _executor;
    private readonly ILogger<CreditNoteWriteRoutine> _logger;

    public CreditNoteWriteRoutine(IRoutineExecutor executor, ILogger<CreditNoteWriteRoutine> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    public Task<long?> GetOutstandingBalanceUnitsAsync(long invoiceId, CancellationToken cancellationToken)
        => _executor.ExecuteScalarAsync<long?>(
            SqlScriptCatalog.OutstandingBalance,
            new { InvoiceId = invoiceId },
            cancellationToken);

    public Task<CreditNoteAppendOutcome> AppendCreditNoteAsync(
        CreditNoteAppendRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parameters = new
        {
            request.InvoiceId,
            request.Number,
            CreditNoteDate = SqliteValueFormat.ToStorage(request.CreditNoteDate),
            request.AmountUnits,
            CreatedAtUtc = SqliteValueFormat.ToStorage(request.CreatedAtUtc)
        };

        return _executor.ExecuteInImmediateTransactionAsync(
            async (session, token) =>
            {
                int inserted;
                try
                {
                    inserted = await session
                        .ExecuteAsync(SqlScriptCatalog.AppendCreditNote, parameters, token)
                        .ConfigureAwait(false);
                }
                catch (SqliteException exception) when (exception.SqliteErrorCode == ConstraintViolation)
                {
                    // El indice unico ux_credit_notes_number impide repetir numeros de nota de credito.
                    await session.RollbackAsync(token).ConfigureAwait(false);
                    _logger.LogWarning(
                        "La nota de credito {Number} no se registro (restriccion del esquema).", request.Number);

                    return request.Number <= 0
                        ? CreditNoteAppendOutcome.ExceedsOutstandingBalance
                        : CreditNoteAppendOutcome.DuplicatedNumber;
                }

                if (inserted > 0)
                {
                    _logger.LogInformation(
                        "Nota de credito {Number} registrada en la factura {InvoiceId}.",
                        request.Number,
                        request.InvoiceId);

                    return CreditNoteAppendOutcome.Applied;
                }

                // La insercion no aplico: la rutina de resolucion traduce el motivo al ordinal del enum.
                var reason = await session
                    .ExecuteScalarAsync<int>(SqlScriptCatalog.ResolveCreditNoteRejection, parameters, token)
                    .ConfigureAwait(false);

                await session.RollbackAsync(token).ConfigureAwait(false);

                var outcome = (CreditNoteAppendOutcome)reason;
                _logger.LogWarning(
                    "Nota de credito {Number} rechazada para la factura {InvoiceId}: {Outcome}.",
                    request.Number,
                    request.InvoiceId,
                    outcome);

                return outcome;
            },
            cancellationToken);
    }
}
