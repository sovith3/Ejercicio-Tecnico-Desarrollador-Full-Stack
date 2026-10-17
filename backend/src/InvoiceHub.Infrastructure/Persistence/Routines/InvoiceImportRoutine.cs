using System.Text.Json;
using System.Text.Json.Serialization;
using InvoiceHub.Application.Ports;
using Microsoft.Extensions.Logging;

namespace InvoiceHub.Infrastructure.Persistence.Routines;

/// <summary>
/// Adaptador del puerto <see cref="IInvoiceImportPort"/>: la carga masiva pesada se resuelve dentro del
/// motor de base de datos. El adaptador solo serializa el lote a JSON y encadena las rutinas dentro de
/// una unica transaccion inmediata:
/// <list type="number">
///   <item><description><c>10_sp_import_batch_stage</c>: prepara el lote en la tabla temporal.</description></item>
///   <item><description><c>11_sp_import_batch_merge</c>: combina cliente, factura, detalle, notas de credito y pago de forma set-based.</description></item>
///   <item><description><c>12_sp_import_batch_outcomes</c>: devuelve el desenlace de cada registro.</description></item>
///   <item><description><c>13_sp_import_batch_purge</c>: limpia el lote.</description></item>
/// </list>
/// Asi el costo de la carga queda en el motor (una pasada por tabla y ninguna ida y vuelta por registro)
/// y la aplicacion no necesita conocer el esquema: solo recibe el desenlace por factura.
/// </summary>
public sealed class InvoiceImportRoutine : IInvoiceImportPort
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    private readonly IRoutineExecutor _executor;
    private readonly IClock _clock;
    private readonly ILogger<InvoiceImportRoutine> _logger;

    public InvoiceImportRoutine(
        IRoutineExecutor executor,
        IClock clock,
        ILogger<InvoiceImportRoutine> logger)
    {
        _executor = executor;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<InvoiceImportOutcome>> ImportBatchAsync(
        IReadOnlyList<InvoicePersistRecord> records,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            return Array.Empty<InvoiceImportOutcome>();
        }

        var parameters = new
        {
            // El token aisla el lote: varios procesos pueden cargar en paralelo sobre el mismo archivo.
            BatchToken = Guid.NewGuid().ToString("N"),
            PayloadJson = JsonSerializer.Serialize(records.Select(ToPayload).ToList(), JsonOptions),
            NowUtc = SqliteValueFormat.ToStorage(_clock.UtcNow)
        };

        var outcomes = await _executor.ExecuteInImmediateTransactionAsync(
            async (session, token) =>
            {
                // El staging no se mide con las filas que reporta el controlador: el lote de sentencias
                // (CREATE TEMP + DELETE + INSERT) acumula un conteo de la primera sentencia que no
                // representa al lote. La completitud se verifica con las posiciones que el motor devuelve
                // en el paso de desenlaces, que son su estado real.
                await session
                    .ExecuteAsync(SqlScriptCatalog.ImportBatchStage, parameters, token)
                    .ConfigureAwait(false);

                await session
                    .ExecuteAsync(SqlScriptCatalog.ImportBatchMerge, parameters, token)
                    .ConfigureAwait(false);

                var rows = await session
                    .QueryAsync<BatchOutcomeRow>(SqlScriptCatalog.ImportBatchOutcomes, parameters, token)
                    .ConfigureAwait(false);

                await session
                    .ExecuteAsync(SqlScriptCatalog.ImportBatchPurge, parameters, token)
                    .ConfigureAwait(false);

                return MapRows(rows, records);
            },
            cancellationToken)
            .ConfigureAwait(false);

        var applied = outcomes.Count(outcome => outcome.Result == InvoiceImportResult.Applied);
        _logger.LogInformation(
            "Lote de {Total} factura(s) procesado en el motor: {Applied} aplicada(s), {Skipped} ya existente(s).",
            outcomes.Count,
            applied,
            outcomes.Count - applied);

        return outcomes;
    }

    /// <summary>
    /// Traduce el contrato de aplicacion al documento JSON que consume la rutina. Las fechas se envian
    /// ya en el formato de almacenamiento del esquema, de modo que el motor inserte exactamente el mismo
    /// TEXTO que el proveedor de EF Core escribio en las demas filas.
    /// </summary>
    private static BatchRecord ToPayload(InvoicePersistRecord record) => new()
    {
        Number = record.Number,
        InvoiceDate = SqliteValueFormat.ToStorage(record.InvoiceDate),
        TotalAmountUnits = record.TotalAmountUnits,
        DaysToDue = record.DaysToDue,
        PaymentDueDate = SqliteValueFormat.ToStorage(record.PaymentDueDate),
        Status = (int)record.Status,
        PaymentStatus = (int)record.PaymentStatus,
        IsConsistent = record.IsConsistent ? 1 : 0,
        InconsistencyReason = record.InconsistencyReason,
        DeclaredStatus = record.DeclaredStatus is { } declaredStatus ? (int)declaredStatus : null,
        DeclaredPaymentStatus = record.DeclaredPaymentStatus is { } declaredPayment
            ? (int)declaredPayment
            : null,
        CustomerRun = record.CustomerRun,
        CustomerName = record.CustomerName,
        CustomerEmail = record.CustomerEmail,
        CreatedAtUtc = SqliteValueFormat.ToStorage(record.CreatedAtUtc),
        UpdatedAtUtc = SqliteValueFormat.ToStorage(record.UpdatedAtUtc),
        Details = record.Details
            .Select(detail => new BatchDetail
            {
                ProductName = detail.ProductName,
                UnitPriceUnits = detail.UnitPriceUnits,
                Quantity = detail.Quantity,
                SubtotalUnits = detail.SubtotalUnits
            })
            .ToList(),
        CreditNotes = record.CreditNotes
            .Select(note => new BatchCreditNote
            {
                Number = note.Number,
                CreditNoteDate = SqliteValueFormat.ToStorage(note.CreditNoteDate),
                AmountUnits = note.AmountUnits,
                CreatedAtUtc = SqliteValueFormat.ToStorage(note.CreatedAtUtc)
            })
            .ToList(),
        Payment = record.Payment is null
            ? null
            : new BatchPayment
            {
                PaymentMethod = record.Payment.PaymentMethod,
                PaymentDate = SqliteValueFormat.ToStorage(record.Payment.PaymentDate),
                RecordedAtUtc = SqliteValueFormat.ToStorage(record.Payment.RecordedAtUtc)
            }
    };

    /// <summary>
    /// Verifica que el motor haya devuelto un desenlace por registro y en el mismo orden en que se
    /// enviaron: si no coincide, el lote no es confiable y se aborta (la transaccion no se confirma).
    /// </summary>
    private static IReadOnlyList<InvoiceImportOutcome> MapRows(
        IReadOnlyList<BatchOutcomeRow> rows,
        IReadOnlyList<InvoicePersistRecord> records)
    {
        if (rows.Count != records.Count)
        {
            throw new InvalidOperationException(
                $"La rutina de carga devolvio {rows.Count} resultado(s) para {records.Count} factura(s) del lote.");
        }

        var outcomes = new List<InvoiceImportOutcome>(rows.Count);

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];

            // La posicion la asigna el motor al preparar el lote: si falta o sobra una fila, o el orden no
            // corresponde, el lote no es confiable y la transaccion no se confirma.
            if (row.SourcePosition != index || row.Number != records[index].Number)
            {
                throw new InvalidOperationException(
                    $"La rutina de carga devolvio la posicion {row.SourcePosition} con la factura {row.Number} " +
                    $"donde se esperaba la posicion {index} con la factura {records[index].Number}.");
            }

            outcomes.Add(new InvoiceImportOutcome(
                (int)row.Number,
                (InvoiceImportResult)(int)row.Outcome,
                row.InvoiceId));
        }

        return outcomes;
    }

    /// <summary>
    /// Desenlace devuelto por la rutina de carga para un registro del lote. Se materializa por
    /// propiedades (no por constructor): SQLite entrega todos los enteros como <c>Int64</c> y el mapeo por
    /// propiedades admite tanto esa amplitud como un identificador nulo.
    /// </summary>
    private sealed class BatchOutcomeRow
    {
        public long SourcePosition { get; init; }

        public long Number { get; init; }

        public long Outcome { get; init; }

        public long? InvoiceId { get; init; }
    }

    /// <summary>Factura del lote tal como la recibe la rutina: los nombres de propiedad son el contrato JSON que extrae el SQL.</summary>
    private sealed record BatchRecord
    {
        [JsonPropertyName("number")]
        public int Number { get; init; }

        [JsonPropertyName("invoiceDate")]
        public string InvoiceDate { get; init; } = string.Empty;

        [JsonPropertyName("totalAmountUnits")]
        public long TotalAmountUnits { get; init; }

        [JsonPropertyName("daysToDue")]
        public int DaysToDue { get; init; }

        [JsonPropertyName("paymentDueDate")]
        public string PaymentDueDate { get; init; } = string.Empty;

        [JsonPropertyName("status")]
        public int Status { get; init; }

        [JsonPropertyName("paymentStatus")]
        public int PaymentStatus { get; init; }

        [JsonPropertyName("isConsistent")]
        public int IsConsistent { get; init; }

        [JsonPropertyName("inconsistencyReason")]
        public string? InconsistencyReason { get; init; }

        [JsonPropertyName("declaredStatus")]
        public int? DeclaredStatus { get; init; }

        [JsonPropertyName("declaredPaymentStatus")]
        public int? DeclaredPaymentStatus { get; init; }

        [JsonPropertyName("customerRun")]
        public string CustomerRun { get; init; } = string.Empty;

        [JsonPropertyName("customerName")]
        public string CustomerName { get; init; } = string.Empty;

        [JsonPropertyName("customerEmail")]
        public string CustomerEmail { get; init; } = string.Empty;

        [JsonPropertyName("createdAtUtc")]
        public string CreatedAtUtc { get; init; } = string.Empty;

        [JsonPropertyName("updatedAtUtc")]
        public string UpdatedAtUtc { get; init; } = string.Empty;

        [JsonPropertyName("details")]
        public IReadOnlyList<BatchDetail> Details { get; init; } = Array.Empty<BatchDetail>();

        [JsonPropertyName("creditNotes")]
        public IReadOnlyList<BatchCreditNote> CreditNotes { get; init; } = Array.Empty<BatchCreditNote>();

        [JsonPropertyName("payment")]
        public BatchPayment? Payment { get; init; }
    }

    private sealed record BatchDetail
    {
        [JsonPropertyName("productName")]
        public string ProductName { get; init; } = string.Empty;

        [JsonPropertyName("unitPriceUnits")]
        public long UnitPriceUnits { get; init; }

        [JsonPropertyName("quantity")]
        public int Quantity { get; init; }

        [JsonPropertyName("subtotalUnits")]
        public long SubtotalUnits { get; init; }
    }

    private sealed record BatchCreditNote
    {
        [JsonPropertyName("number")]
        public int Number { get; init; }

        [JsonPropertyName("creditNoteDate")]
        public string CreditNoteDate { get; init; } = string.Empty;

        [JsonPropertyName("amountUnits")]
        public long AmountUnits { get; init; }

        [JsonPropertyName("createdAtUtc")]
        public string CreatedAtUtc { get; init; } = string.Empty;
    }

    private sealed record BatchPayment
    {
        [JsonPropertyName("paymentMethod")]
        public string PaymentMethod { get; init; } = string.Empty;

        [JsonPropertyName("paymentDate")]
        public string PaymentDate { get; init; } = string.Empty;

        [JsonPropertyName("recordedAtUtc")]
        public string RecordedAtUtc { get; init; } = string.Empty;
    }
}
