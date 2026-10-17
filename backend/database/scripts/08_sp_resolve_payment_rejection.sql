-- =============================================================================================
-- InvoiceHub - sp_ResolvePaymentRejection
-- ---------------------------------------------------------------------------------------------
-- Se ejecuta UNICAMENTE cuando sp_RegisterPayment no inserto la fila (0 filas afectadas), para
-- traducir el motivo al ordinal del enum PaymentAppendOutcome:
--   1 InvoiceNotFound | 2 InvoiceInconsistent | 3 InvoiceCancelled | 4 AlreadyPaid
-- =============================================================================================

SELECT CASE
        WHEN NOT EXISTS (SELECT 1 FROM invoices WHERE Id = @InvoiceId) THEN 1
        WHEN (SELECT IsConsistent FROM invoices WHERE Id = @InvoiceId) = 0 THEN 2
        WHEN (SELECT Status FROM invoices WHERE Id = @InvoiceId) = 2 THEN 3
        ELSE 4
    END;
