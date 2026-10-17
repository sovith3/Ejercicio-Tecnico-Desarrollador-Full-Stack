-- =============================================================================================
-- InvoiceHub - sp_ImportBatchMerge
-- ---------------------------------------------------------------------------------------------
-- Paso 2 de la carga masiva: el nucleo del trabajo pesado, resuelto integramente en el motor y con
-- cinco sentencias set-based (una por tabla) en lugar de una por factura.
--
--   1. Clientes   : alta idempotente por RUN (la primera aparicion del lote define los datos).
--   2. Desenlace  : marca como ya existente el numero que ya esta persistido o que se repite dentro
--                   del propio lote (carga idempotente: la decision vive en la base de datos).
--   3. Facturas   : una unica insercion para todas las facturas nuevas del lote.
--   4. Detalle    : lineas de producto de las facturas insertadas.
--   5. Creditos   : notas de credito y el pago informado por el origen.
--
-- Los valores derivados (Status, PaymentStatus, IsConsistent) llegan ya calculados por el dominio y
-- se conservan aqui tal cual: la unica autoridad que los recalcula es sp_RecalculateInvoiceStatuses,
-- que la importacion ejecuta al final sobre el universo completo.
--
-- El adaptador ejecuta esta rutina dentro de la MISMA transaccion inmediata que el staging y los
-- desenlaces: el lote se aplica completo o no se aplica.
-- =============================================================================================

-- 1) Clientes: alta idempotente por RUN.
INSERT INTO customers (Run, Name, Email, CreatedAtUtc)
SELECT staged.CustomerRun, staged.CustomerName, staged.CustomerEmail, @NowUtc
FROM stg_invoice_import AS staged
WHERE staged.BatchToken = @BatchToken
  AND staged.SourcePosition = (
        SELECT MIN(first_seen.SourcePosition)
        FROM stg_invoice_import AS first_seen
        WHERE first_seen.BatchToken = staged.BatchToken
          AND first_seen.CustomerRun = staged.CustomerRun)
  AND NOT EXISTS (SELECT 1 FROM customers AS existing WHERE existing.Run = staged.CustomerRun);

-- 2) Desenlace: numero ya persistido (o repetido dentro del lote) => se omite.
UPDATE stg_invoice_import
SET Outcome = 1
WHERE BatchToken = @BatchToken
  AND (EXISTS (SELECT 1 FROM invoices AS persisted WHERE persisted.Number = stg_invoice_import.Number)
       OR SourcePosition > (
            SELECT MIN(duplicated.SourcePosition)
            FROM stg_invoice_import AS duplicated
            WHERE duplicated.BatchToken = stg_invoice_import.BatchToken
              AND duplicated.Number = stg_invoice_import.Number));

-- 3) Facturas nuevas del lote, en una sola pasada.
INSERT INTO invoices (
    Number, InvoiceDate, TotalAmountUnits, DaysToDue, PaymentDueDate, Status, PaymentStatus,
    IsConsistent, InconsistencyReason, DeclaredStatus, DeclaredPaymentStatus, CustomerId,
    CreatedAtUtc, UpdatedAtUtc)
SELECT
    staged.Number,
    staged.InvoiceDate,
    staged.TotalAmountUnits,
    staged.DaysToDue,
    staged.PaymentDueDate,
    staged.Status,
    staged.PaymentStatus,
    staged.IsConsistent,
    staged.InconsistencyReason,
    staged.DeclaredStatus,
    staged.DeclaredPaymentStatus,
    customer.Id,
    staged.CreatedAtUtc,
    staged.UpdatedAtUtc
FROM stg_invoice_import AS staged
INNER JOIN customers AS customer ON customer.Run = staged.CustomerRun
WHERE staged.BatchToken = @BatchToken
  AND staged.Outcome = 0;

-- 4) Detalle de producto de las facturas insertadas (el arreglo JSON se expande dentro del motor).
INSERT INTO invoice_details (InvoiceId, ProductName, UnitPriceUnits, Quantity, SubtotalUnits)
SELECT
    invoice.Id,
    json_extract(line.value, '$.productName'),
    CAST(json_extract(line.value, '$.unitPriceUnits') AS INTEGER),
    CAST(json_extract(line.value, '$.quantity') AS INTEGER),
    CAST(json_extract(line.value, '$.subtotalUnits') AS INTEGER)
FROM stg_invoice_import AS staged
INNER JOIN invoices AS invoice ON invoice.Number = staged.Number
INNER JOIN json_each(staged.DetailsJson) AS line
WHERE staged.BatchToken = @BatchToken
  AND staged.Outcome = 0;

-- 5a) Notas de credito del origen.
INSERT INTO credit_notes (InvoiceId, Number, CreditNoteDate, AmountUnits, CreatedAtUtc)
SELECT
    invoice.Id,
    CAST(json_extract(note.value, '$.number') AS INTEGER),
    json_extract(note.value, '$.creditNoteDate'),
    CAST(json_extract(note.value, '$.amountUnits') AS INTEGER),
    json_extract(note.value, '$.createdAtUtc')
FROM stg_invoice_import AS staged
INNER JOIN invoices AS invoice ON invoice.Number = staged.Number
INNER JOIN json_each(staged.CreditNotesJson) AS note
WHERE staged.BatchToken = @BatchToken
  AND staged.Outcome = 0;

-- 5b) Pago informado por el origen (a lo sumo uno por factura).
INSERT INTO invoice_payments (InvoiceId, PaymentMethod, PaymentDate, RecordedAtUtc)
SELECT
    invoice.Id,
    json_extract(staged.PaymentJson, '$.paymentMethod'),
    json_extract(staged.PaymentJson, '$.paymentDate'),
    json_extract(staged.PaymentJson, '$.recordedAtUtc')
FROM stg_invoice_import AS staged
INNER JOIN invoices AS invoice ON invoice.Number = staged.Number
WHERE staged.BatchToken = @BatchToken
  AND staged.Outcome = 0
  AND staged.PaymentJson IS NOT NULL;

-- 6) Identificador resultante de cada numero (el recien insertado o el ya existente) para informar
--    el desenlace a la aplicacion.
UPDATE stg_invoice_import
SET InvoiceId = (SELECT invoice.Id FROM invoices AS invoice WHERE invoice.Number = stg_invoice_import.Number)
WHERE BatchToken = @BatchToken;
