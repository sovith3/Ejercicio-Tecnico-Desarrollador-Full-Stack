-- =============================================================================================
-- InvoiceHub - reporte: resumen por estado de cobro (reporte 2)
-- ---------------------------------------------------------------------------------------------
-- Agrega cantidad de facturas y monto facturado por estado de cobro y calcula los porcentajes con
-- funciones de ventana (SUM(...) OVER ()), de modo que el calculo ocurre en el motor y no en la
-- aplicacion. Se consideran unicamente facturas consistentes (sistema activo).
-- =============================================================================================

WITH resumen AS (
    SELECT
        v.PaymentStatus                        AS PaymentStatus,
        COUNT(*)                               AS InvoiceCount,
        COALESCE(SUM(v.TotalAmountUnits), 0)   AS TotalAmountUnits
    FROM vw_invoice_search v
    WHERE v.IsConsistent = 1
    GROUP BY v.PaymentStatus
)
SELECT
    PaymentStatus,
    InvoiceCount,
    TotalAmountUnits,
    CASE WHEN SUM(TotalAmountUnits) OVER () > 0
         THEN 100.0 * TotalAmountUnits / SUM(TotalAmountUnits) OVER ()
         ELSE 0 END AS AmountPercentage,
    CASE WHEN SUM(InvoiceCount) OVER () > 0
         THEN 100.0 * InvoiceCount / SUM(InvoiceCount) OVER ()
         ELSE 0 END AS CountPercentage
FROM resumen
ORDER BY PaymentStatus;
