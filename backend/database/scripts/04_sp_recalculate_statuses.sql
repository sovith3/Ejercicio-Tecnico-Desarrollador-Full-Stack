-- =============================================================================================
-- InvoiceHub - sp_RecalculateInvoiceStatuses
-- ---------------------------------------------------------------------------------------------
-- Recalcula de forma CONJUNTA (set-based, sin traer datos a la aplicacion) los tres valores
-- derivados de cada factura:
--
--   * Status          -> a partir de la suma de notas de credito (regla InvoiceStatusRules)
--                            0 Issued    : sin notas de credito
--                            1 Partial   : notas de credito menores al total
--                            2 Cancelled : notas de credito iguales o mayores al total
--   * PaymentStatus   -> 2 Paid si existe pago; si no, 1 Overdue cuando la fecha de vencimiento es
--                        anterior a la fecha de negocio (@Today); en cualquier otro caso 0 Pending.
--   * IsConsistent    -> 1 cuando el total declarado coincide exactamente con la suma de los
--                        subtotales de las lineas de detalle. La rutina MARCA inconsistencias
--                        detectadas por montos, pero no limpia las ya registradas por otras causas
--                        (por ejemplo una nota de credito que superaba el saldo pendiente), porque
--                        ambas mantienen la factura fuera del sistema activo.
--
-- Los montos se comparan en unidades enteras (1 peso = 100000 unidades, cinco decimales) y el motivo
-- describe los importes con separadores chilenos (punto de miles y coma decimal).
--
-- Parametros:
--   @Today     (TEXT   yyyy-MM-dd) fecha de negocio del reloj de la aplicacion.
--   @InvoiceId (INTEGER, NULL)     si es NULL recalcula el universo completo; si trae valor,
--                                  recalcula unicamente esa factura.
-- Devuelve: cantidad de facturas evaluadas (filas afectadas).
-- =============================================================================================

UPDATE invoices
SET Status = CASE
        WHEN COALESCE((SELECT SUM(cn.AmountUnits) FROM credit_notes cn WHERE cn.InvoiceId = invoices.Id), 0) <= 0
            THEN 0
        WHEN COALESCE((SELECT SUM(cn.AmountUnits) FROM credit_notes cn WHERE cn.InvoiceId = invoices.Id), 0)
             >= invoices.TotalAmountUnits
            THEN 2
        ELSE 1
    END,
    PaymentStatus = CASE
        WHEN EXISTS (SELECT 1 FROM invoice_payments p WHERE p.InvoiceId = invoices.Id) THEN 2
        WHEN date(@Today) > date(invoices.PaymentDueDate) THEN 1
        ELSE 0
    END,
    IsConsistent = CASE
        WHEN invoices.TotalAmountUnits = COALESCE(
                (SELECT SUM(d.SubtotalUnits) FROM invoice_details d WHERE d.InvoiceId = invoices.Id), 0)
            THEN invoices.IsConsistent
        ELSE 0
    END,
    InconsistencyReason = CASE
        WHEN invoices.TotalAmountUnits = COALESCE(
                (SELECT SUM(d.SubtotalUnits) FROM invoice_details d WHERE d.InvoiceId = invoices.Id), 0)
            THEN invoices.InconsistencyReason
        WHEN invoices.InconsistencyReason IS NULL
            THEN 'El total declarado (' ||
                 replace(replace(replace(
                     printf('%,.2f', invoices.TotalAmountUnits / 100000.0),
                     ',', '#'), '.', ','), '#', '.') ||
                 ') no coincide con la suma de subtotales de sus productos (' ||
                 replace(replace(replace(
                     printf('%,.2f', COALESCE((SELECT SUM(d.SubtotalUnits) FROM invoice_details d WHERE d.InvoiceId = invoices.Id), 0) / 100000.0),
                     ',', '#'), '.', ','), '#', '.') ||
                 ').'
        ELSE invoices.InconsistencyReason
    END
WHERE (@InvoiceId IS NULL OR invoices.Id = @InvoiceId);
