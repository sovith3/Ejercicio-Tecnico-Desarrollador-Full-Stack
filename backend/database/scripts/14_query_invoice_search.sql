-- =============================================================================================
-- InvoiceHub - consulta de busqueda de facturas (paginada)
-- ---------------------------------------------------------------------------------------------
-- Filtra, ordena y pagina sobre vw_invoice_search. El ordenamiento se resuelve con expresiones
-- CASE alimentadas por @SortBy / @SortDirection; la capa de aplicacion valida previamente ambos
-- valores contra una lista blanca (InvoiceSearchCriteriaNormalizer), de modo que nunca se
-- interpola texto en la sentencia.
--
-- Parametros: @Number, @InvoiceStatus, @PaymentStatus, @IsConsistent, @OnlyActiveSystem,
--             @CustomerRunLike, @TextLike, @MinDaysOverdue, @Today, @SortBy, @SortDirection,
--             @PageSize, @Offset. Los filtros opcionales viajan como NULL.
-- La clausula WHERE es identica a la de 15_query_invoice_search_count.sql: cualquier cambio debe
-- replicarse en ambos scripts.
-- =============================================================================================

SELECT
    v.InvoiceId,
    v.Number,
    v.InvoiceDate,
    v.PaymentDueDate,
    v.DaysToDue,
    v.TotalAmountUnits,
    v.CreditNotesTotalUnits,
    v.OutstandingBalanceUnits,
    v.CustomerRun,
    v.CustomerName,
    v.CustomerEmail,
    v.InvoiceStatus,
    v.PaymentStatus,
    v.IsConsistent,
    v.InconsistencyReason,
    v.HasPayment,
    v.PaymentMethod,
    v.PaymentDate,
    v.CreditNoteCount,
    CASE WHEN date(@Today) > date(v.PaymentDueDate)
         THEN CAST(julianday(date(@Today)) - julianday(date(v.PaymentDueDate)) AS INTEGER)
         ELSE 0
    END AS DaysOverdue
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
       OR date(v.PaymentDueDate) <= date(@Today, '+' || @MinDaysOverdue || ' day'))
ORDER BY
    CASE WHEN @SortBy = 'number'       AND @SortDirection = 'asc'  THEN v.Number                END ASC,
    CASE WHEN @SortBy = 'number'       AND @SortDirection = 'desc' THEN v.Number                END DESC,
    CASE WHEN @SortBy = 'invoiceDate'  AND @SortDirection = 'asc'  THEN v.InvoiceDate           END ASC,
    CASE WHEN @SortBy = 'invoiceDate'  AND @SortDirection = 'desc' THEN v.InvoiceDate           END DESC,
    CASE WHEN @SortBy = 'dueDate'      AND @SortDirection = 'asc'  THEN v.PaymentDueDate        END ASC,
    CASE WHEN @SortBy = 'dueDate'      AND @SortDirection = 'desc' THEN v.PaymentDueDate        END DESC,
    CASE WHEN @SortBy = 'customer'     AND @SortDirection = 'asc'  THEN v.CustomerName          END ASC,
    CASE WHEN @SortBy = 'customer'     AND @SortDirection = 'desc' THEN v.CustomerName          END DESC,
    CASE WHEN @SortBy = 'total'        AND @SortDirection = 'asc'  THEN v.TotalAmountUnits      END ASC,
    CASE WHEN @SortBy = 'total'        AND @SortDirection = 'desc' THEN v.TotalAmountUnits      END DESC,
    CASE WHEN @SortBy = 'outstanding'  AND @SortDirection = 'asc'  THEN v.OutstandingBalanceUnits END ASC,
    CASE WHEN @SortBy = 'outstanding'  AND @SortDirection = 'desc' THEN v.OutstandingBalanceUnits END DESC,
    CASE WHEN @SortBy = 'creditNotes'  AND @SortDirection = 'asc'  THEN v.CreditNotesTotalUnits END ASC,
    CASE WHEN @SortBy = 'creditNotes'  AND @SortDirection = 'desc' THEN v.CreditNotesTotalUnits END DESC,
    CASE WHEN @SortBy = 'daysOverdue'  AND @SortDirection = 'asc'
         THEN CAST(julianday(date(@Today)) - julianday(date(v.PaymentDueDate)) AS INTEGER)      END ASC,
    CASE WHEN @SortBy = 'daysOverdue'  AND @SortDirection = 'desc'
         THEN CAST(julianday(date(@Today)) - julianday(date(v.PaymentDueDate)) AS INTEGER)      END DESC,
    CASE WHEN @SortBy = 'status'       AND @SortDirection = 'asc'  THEN v.InvoiceStatus         END ASC,
    CASE WHEN @SortBy = 'status'       AND @SortDirection = 'desc' THEN v.InvoiceStatus         END DESC,
    v.Number DESC
LIMIT @PageSize OFFSET @Offset;
