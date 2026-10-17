using InvoiceHub.Application.Ports;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace InvoiceHub.Infrastructure.Persistence.Routines;

/// <summary>
/// Adaptador del puerto <see cref="IPaymentWritePort"/>: registra el pago validando en la misma
/// sentencia SQL que la factura exista, este activa, no este cancelada y no tenga un pago previo.
/// </summary>
public sealed class PaymentWriteRoutine : IPaymentWritePort
{
    /// <summary>Codigo de error de SQLite para violaciones de restriccion (indices unicos).</summary>
    private const int ConstraintViolation = 19;

    private readonly IRoutineExecutor _executor;
    private readonly ILogger<PaymentWriteRoutine> _logger;

    public PaymentWriteRoutine(IRoutineExecutor executor, ILogger<PaymentWriteRoutine> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    public Task<PaymentAppendOutcome> RegisterPaymentAsync(
        PaymentAppendRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parameters = new
        {
            request.InvoiceId,
            request.PaymentMethod,
            PaymentDate = SqliteValueFormat.ToStorage(request.PaymentDate),
            RecordedAtUtc = SqliteValueFormat.ToStorage(request.RecordedAtUtc)
        };

        return _executor.ExecuteInImmediateTransactionAsync(
            async (session, token) =>
            {
                int inserted;
                try
                {
                    inserted = await session
                        .ExecuteAsync(SqlScriptCatalog.RegisterPayment, parameters, token)
                        .ConfigureAwait(false);
                }
                catch (SqliteException exception) when (exception.SqliteErrorCode == ConstraintViolation)
                {
                    // El indice unico ux_invoice_payments_invoice impide dos pagos sobre la misma factura.
                    await session.RollbackAsync(token).ConfigureAwait(false);
                    _logger.LogWarning(
                        "Pago rechazado para la factura {InvoiceId}: ya existe un pago registrado.",
                        request.InvoiceId);

                    return PaymentAppendOutcome.AlreadyPaid;
                }

                if (inserted > 0)
                {
                    _logger.LogInformation(
                        "Pago de la factura {InvoiceId} registrado con fecha {PaymentDate}.",
                        request.InvoiceId,
                        request.PaymentDate);

                    return PaymentAppendOutcome.Applied;
                }

                var reason = await session
                    .ExecuteScalarAsync<int>(SqlScriptCatalog.ResolvePaymentRejection, parameters, token)
                    .ConfigureAwait(false);

                await session.RollbackAsync(token).ConfigureAwait(false);

                var outcome = (PaymentAppendOutcome)reason;
                _logger.LogWarning(
                    "Pago rechazado para la factura {InvoiceId}: {Outcome}.", request.InvoiceId, outcome);

                return outcome;
            },
            cancellationToken);
    }
}
