-- =============================================================================================
-- InvoiceHub - Indices
-- ---------------------------------------------------------------------------------------------
-- Los indices se declaran en un script aparte para poder aplicarlos despues de la carga masiva del
-- archivo de origen (bulk insert) y para evidenciar que las consultas de busqueda y de reportes
-- estan respaldadas por indices.
-- =============================================================================================

-- Identificadores naturales unicos exigidos por las bases del examen.
CREATE UNIQUE INDEX IF NOT EXISTS ux_invoices_number      ON invoices (Number);
CREATE UNIQUE INDEX IF NOT EXISTS ux_credit_notes_number  ON credit_notes (Number);
CREATE UNIQUE INDEX IF NOT EXISTS ux_customers_run        ON customers (Run);

-- Una factura puede tener como maximo un pago registrado.
CREATE UNIQUE INDEX IF NOT EXISTS ux_invoice_payments_invoice ON invoice_payments (InvoiceId);

-- Claves foraneas (SQLite no las indexa automaticamente).
CREATE INDEX IF NOT EXISTS ix_invoices_customer            ON invoices (CustomerId);
CREATE INDEX IF NOT EXISTS ix_invoice_details_invoice      ON invoice_details (InvoiceId);
CREATE INDEX IF NOT EXISTS ix_credit_notes_invoice         ON credit_notes (InvoiceId);
CREATE INDEX IF NOT EXISTS ix_import_run_issues_run        ON import_run_issues (ImportRunId, Position);
CREATE INDEX IF NOT EXISTS ix_app_users_username           ON app_users (Username);

-- Filtros mas frecuentes de la busqueda y de los reportes.
CREATE INDEX IF NOT EXISTS ix_invoices_status              ON invoices (Status, PaymentStatus);
CREATE INDEX IF NOT EXISTS ix_invoices_consistent          ON invoices (IsConsistent, PaymentDueDate);
CREATE INDEX IF NOT EXISTS ix_invoices_due_date            ON invoices (PaymentDueDate);
CREATE INDEX IF NOT EXISTS ix_invoices_invoice_date        ON invoices (InvoiceDate);
CREATE INDEX IF NOT EXISTS ix_invoices_declared_status     ON invoices (DeclaredStatus, DeclaredPaymentStatus);
CREATE INDEX IF NOT EXISTS ix_import_runs_started          ON import_runs (StartedAtUtc DESC);
