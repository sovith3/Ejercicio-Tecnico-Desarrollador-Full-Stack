-- =============================================================================================
-- InvoiceHub - reporte: facturas vencidas sin pago ni nota de credito (reporte 1)
-- ---------------------------------------------------------------------------------------------
-- Facturas del sistema activo cuya fecha de vencimiento ya paso por mas de @MinDaysOverdue dias,
-- que no registran pago y que no tienen notas de credito. Es el insumo de la gestion de cobranza.
-- La mora se calcula con la fecha de negocio (@Today) que entrega el reloj de la aplicacion.
-- =============================================================================================

SELECT
    v.Number                                   AS Number,
    v.InvoiceDate                              AS InvoiceDate,
    v.PaymentDueDate                           AS PaymentDueDate,
    CAST(julianday(date(@Today)) - julianday(date(v.PaymentDueDate)) AS INTEGER) AS DaysOverdue,
    v.TotalAmountUnits                         AS TotalAmountUnits,
    v.CustomerRun                              AS CustomerRun,
    v.CustomerName                             AS CustomerName,
    v.CustomerEmail                            AS CustomerEmail,
    v.InvoiceStatus                            AS InvoiceStatus,
    v.PaymentStatus                            AS PaymentStatus
FROM vw_invoice_search v
WHERE v.IsConsistent = 1
  AND v.HasPayment = 0
  AND v.CreditNoteCount = 0
  AND v.OutstandingBalanceUnits > 0
  AND CAST(julianday(date(@Today)) - julianday(date(v.PaymentDueDate)) AS INTEGER) > @MinDaysOverdue
ORDER BY DaysOverdue DESC, v.TotalAmountUnits DESC, v.Number;
