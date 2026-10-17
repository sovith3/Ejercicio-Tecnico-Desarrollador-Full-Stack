-- =============================================================================================
-- InvoiceHub - sp_ImportBatchStage
-- ---------------------------------------------------------------------------------------------
-- Paso 1 de la carga masiva. Convierte el lote recibido desde la aplicacion (un documento JSON) en
-- filas de la tabla temporal stg_invoice_import, de modo que los pasos siguientes (merge y desenlaces)
-- trabajen de forma SET-BASED y no con una sentencia por factura.
--
-- La tabla es TEMPORAL: vive en la conexion de la rutina y desaparece con ella, por lo que no forma
-- parte del esquema persistente ni requiere mantenimiento entre ejecuciones.
--
-- Parametros:
--   @BatchToken  (TEXT) token que aisla el lote; permite cargas concurrentes sobre el mismo archivo.
--   @PayloadJson (TEXT) arreglo JSON con una entrada por factura del lote.
-- Nota: el controlador informa un conteo de filas para el lote de sentencias que NO representa al lote
-- (acumula el conteo de la sentencia DDL); por eso la completitud se verifica en sp_ImportBatchOutcomes,
-- que devuelve la posicion real de cada fila preparada.
-- =============================================================================================

CREATE TEMP TABLE IF NOT EXISTS stg_invoice_import (
    BatchToken            TEXT    NOT NULL,
    SourcePosition        INTEGER NOT NULL,
    Number                INTEGER NOT NULL,
    InvoiceDate           TEXT    NOT NULL,
    TotalAmountUnits      INTEGER NOT NULL,
    DaysToDue             INTEGER NOT NULL,
    PaymentDueDate        TEXT    NOT NULL,
    Status                INTEGER NOT NULL,
    PaymentStatus         INTEGER NOT NULL,
    IsConsistent          INTEGER NOT NULL,
    InconsistencyReason   TEXT    NULL,
    DeclaredStatus        INTEGER NULL,
    DeclaredPaymentStatus INTEGER NULL,
    CustomerRun           TEXT    NOT NULL,
    CustomerName          TEXT    NOT NULL,
    CustomerEmail         TEXT    NOT NULL,
    CreatedAtUtc          TEXT    NOT NULL,
    UpdatedAtUtc          TEXT    NOT NULL,
    DetailsJson           TEXT    NOT NULL,
    CreditNotesJson       TEXT    NOT NULL,
    PaymentJson           TEXT    NULL,
    Outcome               INTEGER NOT NULL DEFAULT 0,
    InvoiceId             INTEGER NULL
);

-- Reproceso del mismo token (por ejemplo, un reintento del lote): se parte de cero.
DELETE FROM stg_invoice_import WHERE BatchToken = @BatchToken;

INSERT INTO stg_invoice_import (
    BatchToken, SourcePosition, Number, InvoiceDate, TotalAmountUnits, DaysToDue, PaymentDueDate,
    Status, PaymentStatus, IsConsistent, InconsistencyReason, DeclaredStatus, DeclaredPaymentStatus,
    CustomerRun, CustomerName, CustomerEmail, CreatedAtUtc, UpdatedAtUtc,
    DetailsJson, CreditNotesJson, PaymentJson, Outcome, InvoiceId)
SELECT
    @BatchToken,
    CAST(batch_entry.key AS INTEGER),
    CAST(json_extract(batch_entry.value, '$.number') AS INTEGER),
    json_extract(batch_entry.value, '$.invoiceDate'),
    CAST(json_extract(batch_entry.value, '$.totalAmountUnits') AS INTEGER),
    CAST(json_extract(batch_entry.value, '$.daysToDue') AS INTEGER),
    json_extract(batch_entry.value, '$.paymentDueDate'),
    CAST(json_extract(batch_entry.value, '$.status') AS INTEGER),
    CAST(json_extract(batch_entry.value, '$.paymentStatus') AS INTEGER),
    CAST(json_extract(batch_entry.value, '$.isConsistent') AS INTEGER),
    json_extract(batch_entry.value, '$.inconsistencyReason'),
    CAST(json_extract(batch_entry.value, '$.declaredStatus') AS INTEGER),
    CAST(json_extract(batch_entry.value, '$.declaredPaymentStatus') AS INTEGER),
    json_extract(batch_entry.value, '$.customerRun'),
    json_extract(batch_entry.value, '$.customerName'),
    json_extract(batch_entry.value, '$.customerEmail'),
    json_extract(batch_entry.value, '$.createdAtUtc'),
    json_extract(batch_entry.value, '$.updatedAtUtc'),
    json_extract(batch_entry.value, '$.details'),
    json_extract(batch_entry.value, '$.creditNotes'),
    json_extract(batch_entry.value, '$.payment'),
    0,
    NULL
FROM json_each(@PayloadJson) AS batch_entry;
