-- =============================================================================================
-- InvoiceHub - conteo de facturas por estado de factura
-- ---------------------------------------------------------------------------------------------
-- Alimenta los combos y las tarjetas de filtro de la interfaz. @OnlyConsistent = 1 restringe el
-- conteo al sistema activo (excluye las facturas inconsistentes).
-- =============================================================================================

SELECT
    v.InvoiceStatus                            AS InvoiceStatus,
    COUNT(*)                                   AS InvoiceCount,
    COALESCE(SUM(v.TotalAmountUnits), 0)       AS TotalAmountUnits
FROM vw_invoice_search v
WHERE (@OnlyConsistent = 0 OR v.IsConsistent = 1)
GROUP BY v.InvoiceStatus
ORDER BY v.InvoiceStatus;
