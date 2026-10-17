-- =============================================================================================
-- InvoiceHub - sp_ResolveCreditNoteRejection
-- ---------------------------------------------------------------------------------------------
-- Se ejecuta UNICAMENTE cuando sp_AppendCreditNote no inserto la fila (0 filas afectadas), para
-- traducir el motivo del rechazo al ordinal del enum CreditNoteAppendOutcome que espera la
-- aplicacion:
--   1 InvoiceNotFound | 2 InvoiceInconsistent | 3 InvoiceCancelled | 4 ExceedsOutstandingBalance
--   5 DuplicatedNumber
-- Se evalua en el mismo orden en que la rutina de insercion aplica sus condiciones.
-- =============================================================================================

SELECT CASE
        WHEN NOT EXISTS (SELECT 1 FROM invoices WHERE Id = @InvoiceId) THEN 1
        WHEN EXISTS (SELECT 1 FROM credit_notes WHERE Number = @Number) THEN 5
        WHEN (SELECT IsConsistent FROM invoices WHERE Id = @InvoiceId) = 0 THEN 2
        WHEN (SELECT Status FROM invoices WHERE Id = @InvoiceId) = 2 THEN 3
        ELSE 4
    END;
