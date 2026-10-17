-- =============================================================================================
-- InvoiceHub - reporte: facturas inconsistentes (reporte 3)
-- ---------------------------------------------------------------------------------------------
-- Facturas cuyo total declarado no coincide con la suma de los subtotales de sus productos (o que
-- fueron marcadas como inconsistentes por otra regla de negocio). Se conservan en la base para
-- auditoria pero quedan excluidas del sistema activo, por lo que este reporte es la unica via para
-- conocerlas. La suma del detalle y la cantidad de lineas se calculan en el motor.
-- =============================================================================================

SELECT
    v.Number                                   AS Number,
    v.InvoiceDate                              AS InvoiceDate,
    v.TotalAmountUnits                         AS DeclaredTotalUnits,
    COALESCE(det.SubtotalUnits, 0)             AS DetailsSubtotalUnits,
    COALESCE(det.LineCount, 0)                 AS DetailLineCount,
    v.CustomerRun                              AS CustomerRun,
    v.CustomerName                             AS CustomerName,
    v.InconsistencyReason                      AS InconsistencyReason
FROM vw_invoice_search v
LEFT JOIN (
        SELECT d.InvoiceId          AS InvoiceId,
               SUM(d.SubtotalUnits) AS SubtotalUnits,
               COUNT(*)             AS LineCount
        FROM invoice_details d
        GROUP BY d.InvoiceId
    ) det
       ON det.InvoiceId = v.InvoiceId
WHERE v.IsConsistent = 0
ORDER BY ABS(v.TotalAmountUnits - COALESCE(det.SubtotalUnits, 0)) DESC, v.Number;
