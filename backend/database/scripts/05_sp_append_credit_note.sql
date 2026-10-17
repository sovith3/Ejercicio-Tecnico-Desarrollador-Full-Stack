-- =============================================================================================
-- InvoiceHub - sp_AppendCreditNote
-- ---------------------------------------------------------------------------------------------
-- Inserta una nota de credito validando TODAS las precondiciones dentro de la propia sentencia
-- (INSERT ... SELECT ... WHERE), de modo que dos solicitudes concurrentes no puedan superar el
-- saldo pendiente de la factura. El adaptador la ejecuta dentro de una transaccion inmediata
-- (BEGIN IMMEDIATE), por lo que la verificacion y la insercion son atomicas.
--
-- Precondiciones evaluadas (equivalentes a los ordinales de CreditNoteAppendOutcome):
--   1 InvoiceNotFound            : la factura no existe.
--   2 InvoiceInconsistent        : la factura esta fuera del sistema activo.
--   3 InvoiceCancelled           : la factura ya fue cancelada con notas de credito.
--   4 ExceedsOutstandingBalance  : el monto supera el saldo pendiente.
--   5 DuplicatedNumber           : el numero de nota de credito ya existe (indice unico).
--
-- Devuelve: cantidad de filas insertadas (0 cuando la validacion rechaza la operacion).
-- =============================================================================================

INSERT INTO credit_notes (InvoiceId, Number, CreditNoteDate, AmountUnits, CreatedAtUtc)
SELECT @InvoiceId, @Number, @CreditNoteDate, @AmountUnits, @CreatedAtUtc
WHERE EXISTS (
        SELECT 1
        FROM invoices i
        WHERE i.Id = @InvoiceId
          AND i.IsConsistent = 1
          AND i.Status <> 2
          AND (i.TotalAmountUnits - COALESCE(
                  (SELECT SUM(cn.AmountUnits) FROM credit_notes cn WHERE cn.InvoiceId = i.Id), 0)) >= @AmountUnits
      )
  AND NOT EXISTS (SELECT 1 FROM credit_notes cn WHERE cn.Number = @Number);
