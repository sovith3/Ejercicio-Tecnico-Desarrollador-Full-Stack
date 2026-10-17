-- =============================================================================================
-- InvoiceHub - sp_GetOutstandingBalance
-- ---------------------------------------------------------------------------------------------
-- Saldo pendiente de una factura calculado por el motor: total declarado menos las notas de
-- credito acumuladas, nunca menor que cero. Devuelve NULL cuando la factura no existe.
-- =============================================================================================

SELECT MAX(i.TotalAmountUnits - COALESCE(
        (SELECT SUM(cn.AmountUnits) FROM credit_notes cn WHERE cn.InvoiceId = i.Id), 0), 0)
FROM invoices i
WHERE i.Id = @InvoiceId;
