-- =============================================================================================
-- InvoiceHub - conteo de facturas por estado de cobro
-- ---------------------------------------------------------------------------------------------
-- @OnlyConsistent = 1 restringe el conteo al sistema activo (excluye las facturas inconsistentes).
-- =============================================================================================

SELECT
    v.PaymentStatus                            AS PaymentStatus,
    COUNT(*)                                   AS InvoiceCount,
    COALESCE(SUM(v.TotalAmountUnits), 0)       AS TotalAmountUnits
FROM vw_invoice_search v
WHERE (@OnlyConsistent = 0 OR v.IsConsistent = 1)
GROUP BY v.PaymentStatus
ORDER BY v.PaymentStatus;
