-- =============================================================================================
-- InvoiceHub - panel principal (indicadores consolidados)
-- ---------------------------------------------------------------------------------------------
-- Devuelve CINCO conjuntos de resultados en un orden fijo, que el adaptador lee con QueryMultiple:
--   1) Indicadores globales (una fila)
--   2) Conteo y monto por estado de factura (sistema activo)
--   3) Resumen y porcentajes por estado de cobro (sistema activo)
--   4) Serie mensual de facturacion y notas de credito
--   5) Clientes con mayor saldo pendiente en facturas vencidas
--
-- Parametros: @Today (fecha de negocio), @DueSoonDays (ventana "proxima a vencer"),
--             @MonthlyBillingMonths (meses de la serie), @TopDebtors (cantidad de deudores).
-- =============================================================================================

SELECT
    COUNT(*)                                                                          AS TotalInvoices,
    COALESCE(SUM(CASE WHEN v.IsConsistent = 1 THEN 1 ELSE 0 END), 0)                  AS ActiveInvoices,
    COALESCE(SUM(CASE WHEN v.IsConsistent = 0 THEN 1 ELSE 0 END), 0)                  AS InconsistentInvoices,
    COALESCE(SUM(v.TotalAmountUnits), 0)                                              AS TotalBilledUnits,
    COALESCE(SUM(CASE WHEN v.IsConsistent = 1 THEN v.TotalAmountUnits ELSE 0 END), 0) AS ActiveBilledUnits,
    COALESCE(SUM(CASE WHEN v.IsConsistent = 1 THEN v.OutstandingBalanceUnits ELSE 0 END), 0) AS OutstandingUnits,
    COALESCE(SUM(v.CreditNotesTotalUnits), 0)                                         AS CreditNotesUnits,
    COALESCE(SUM(CASE WHEN v.IsConsistent = 1 AND v.PaymentStatus = 1 THEN 1 ELSE 0 END), 0) AS OverdueCount,
    COALESCE(SUM(CASE WHEN v.IsConsistent = 1 AND v.PaymentStatus = 1 THEN v.OutstandingBalanceUnits ELSE 0 END), 0) AS OverdueAmountUnits,
    COALESCE(SUM(CASE WHEN v.IsConsistent = 1 AND v.PaymentStatus = 0
                       AND date(v.PaymentDueDate) <= date(@Today, '+' || @DueSoonDays || ' day')
                      THEN 1 ELSE 0 END), 0)                                      AS DueSoonCount,
    COALESCE(SUM(CASE WHEN v.IsConsistent = 1 AND v.PaymentStatus = 0
                       AND date(v.PaymentDueDate) <= date(@Today, '+' || @DueSoonDays || ' day')
                      THEN v.OutstandingBalanceUnits ELSE 0 END), 0)              AS DueSoonAmountUnits
FROM vw_invoice_search v;

SELECT
    v.InvoiceStatus                            AS InvoiceStatus,
    COUNT(*)                                   AS InvoiceCount,
    COALESCE(SUM(v.TotalAmountUnits), 0)       AS TotalAmountUnits
FROM vw_invoice_search v
WHERE v.IsConsistent = 1
GROUP BY v.InvoiceStatus
ORDER BY v.InvoiceStatus;

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

-- Serie mensual: la ventana se ancla al mes de la factura mas reciente y no a la fecha de negocio, de
-- modo que el panel sigue mostrando la estacionalidad de la cartera incluso cuando los datos cargados
-- son historicos (una ventana anclada en "hoy" quedaria vacia con un origen desactualizado).
SELECT
    strftime('%Y-%m', v.InvoiceDate)                  AS Period,
    COUNT(*)                                          AS InvoiceCount,
    COALESCE(SUM(v.TotalAmountUnits), 0)              AS TotalAmountUnits,
    COALESCE(SUM(v.CreditNotesTotalUnits), 0)         AS CreditNotesUnits
FROM vw_invoice_search v
WHERE v.IsConsistent = 1
  AND date(v.InvoiceDate) >= (
        SELECT date(MAX(prev.InvoiceDate), '-' || @MonthlyBillingMonths || ' months')
        FROM vw_invoice_search prev
        WHERE prev.IsConsistent = 1
      )
GROUP BY Period
ORDER BY Period;

SELECT
    v.CustomerRun                                 AS CustomerRun,
    v.CustomerName                                AS CustomerName,
    COUNT(*)                                      AS OverdueCount,
    COALESCE(SUM(v.OutstandingBalanceUnits), 0)   AS OutstandingUnits
FROM vw_invoice_search v
WHERE v.IsConsistent = 1
  AND v.PaymentStatus = 1
  AND v.OutstandingBalanceUnits > 0
GROUP BY v.CustomerRun, v.CustomerName
ORDER BY OutstandingUnits DESC, v.CustomerName
LIMIT @TopDebtors;
