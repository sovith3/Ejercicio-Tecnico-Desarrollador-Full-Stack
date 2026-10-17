-- =============================================================================================
-- InvoiceHub - sp_RegisterPayment
-- ---------------------------------------------------------------------------------------------
-- Registra el pago de una factura validando en la misma sentencia que la factura exista, que
-- pertenezca al sistema activo, que no este cancelada y que no tenga ya un pago registrado
-- (indice unico ux_invoice_payments_invoice). La fecha de registro la entrega el reloj de la
-- aplicacion.
--
-- Devuelve: cantidad de filas insertadas (0 cuando la validacion rechaza la operacion).
-- =============================================================================================

INSERT INTO invoice_payments (InvoiceId, PaymentMethod, PaymentDate, RecordedAtUtc)
SELECT @InvoiceId, @PaymentMethod, @PaymentDate, @RecordedAtUtc
WHERE EXISTS (
        SELECT 1
        FROM invoices i
        WHERE i.Id = @InvoiceId
          AND i.IsConsistent = 1
          AND i.Status <> 2
          AND NOT EXISTS (SELECT 1 FROM invoice_payments p WHERE p.InvoiceId = i.Id)
      );
