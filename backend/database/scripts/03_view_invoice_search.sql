-- =============================================================================================
-- InvoiceHub - Vista de busqueda de facturas
-- ---------------------------------------------------------------------------------------------
-- Resuelve dentro del motor de base de datos el join con el cliente, la agregacion de notas de
-- credito, el pago registrado y el saldo pendiente. La capa de aplicacion nunca reconstruye estos
-- valores en memoria: solo filtra, ordena y pagina sobre esta vista.
--
-- Los dias de mora NO se incluyen aqui porque dependen de la fecha de negocio configurada
-- (zona horaria) y una vista no acepta parametros; las consultas que los necesitan calculan
-- julianday(@Today) - julianday(PaymentDueDate) con la fecha del reloj de la aplicacion.
-- =============================================================================================

DROP VIEW IF EXISTS vw_invoice_search;

CREATE VIEW vw_invoice_search AS
SELECT
    i.Id                                                  AS InvoiceId,
    i.Number                                              AS Number,
    i.InvoiceDate                                         AS InvoiceDate,
    i.PaymentDueDate                                      AS PaymentDueDate,
    i.DaysToDue                                           AS DaysToDue,
    i.TotalAmountUnits                                    AS TotalAmountUnits,
    COALESCE(cn.TotalUnits, 0)                            AS CreditNotesTotalUnits,
    MAX(i.TotalAmountUnits - COALESCE(cn.TotalUnits, 0), 0) AS OutstandingBalanceUnits,
    COALESCE(cn.NoteCount, 0)                             AS CreditNoteCount,
    c.Run                                                 AS CustomerRun,
    c.Name                                                AS CustomerName,
    c.Email                                               AS CustomerEmail,
    i.Status                                              AS InvoiceStatus,
    i.PaymentStatus                                       AS PaymentStatus,
    i.IsConsistent                                        AS IsConsistent,
    i.InconsistencyReason                                 AS InconsistencyReason,
    i.DeclaredStatus                                      AS DeclaredStatus,
    i.DeclaredPaymentStatus                               AS DeclaredPaymentStatus,
    CASE WHEN p.Id IS NULL THEN 0 ELSE 1 END              AS HasPayment,
    p.PaymentMethod                                       AS PaymentMethod,
    p.PaymentDate                                         AS PaymentDate,
    p.RecordedAtUtc                                       AS PaymentRecordedAtUtc
FROM invoices i
INNER JOIN customers c
        ON c.Id = i.CustomerId
LEFT JOIN (
        SELECT InvoiceId,
               SUM(AmountUnits) AS TotalUnits,
               COUNT(*)         AS NoteCount
        FROM credit_notes
        GROUP BY InvoiceId
    ) cn
       ON cn.InvoiceId = i.Id
LEFT JOIN invoice_payments p
       ON p.InvoiceId = i.Id;
