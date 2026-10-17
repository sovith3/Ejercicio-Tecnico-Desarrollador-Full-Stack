-- =============================================================================================
-- InvoiceHub - conteo de la busqueda de facturas
-- ---------------------------------------------------------------------------------------------
-- Devuelve la cantidad total de facturas que cumplen los filtros (necesaria para paginar). La
-- clausula WHERE es identica a la de 14_query_invoice_search.sql: cualquier cambio debe
-- replicarse en ambos scripts.
-- =============================================================================================

SELECT COUNT(*)
FROM vw_invoice_search v
WHERE (@Number IS NULL OR v.Number = @Number)
  AND (@InvoiceStatus IS NULL OR v.InvoiceStatus = @InvoiceStatus)
  AND (@PaymentStatus IS NULL OR v.PaymentStatus = @PaymentStatus)
  AND (@IsConsistent IS NULL OR v.IsConsistent = @IsConsistent)
  AND (@OnlyActiveSystem = 0 OR v.IsConsistent = 1)
  AND (@CustomerRunLike IS NULL OR v.CustomerRun LIKE @CustomerRunLike)
  AND (@TextLike IS NULL
       OR v.CustomerName LIKE @TextLike
       OR v.CustomerEmail LIKE @TextLike
       OR EXISTS (SELECT 1 FROM invoice_details d
                  WHERE d.InvoiceId = v.InvoiceId AND d.ProductName LIKE @TextLike))
  AND (@MinDaysOverdue IS NULL
       OR date(v.PaymentDueDate) <= date(@Today, '+' || @MinDaysOverdue || ' day'));
