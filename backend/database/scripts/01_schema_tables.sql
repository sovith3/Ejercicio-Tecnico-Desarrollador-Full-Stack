-- =============================================================================================
-- InvoiceHub - Esquema base
-- ---------------------------------------------------------------------------------------------
-- Convenciones:
--   * Los montos se almacenan como enteros escalados a cinco decimales de peso (1 peso = 100000
--     unidades): aritmetica exacta en el motor y sin depender de tipos de punto flotante.
--   * Las fechas de negocio se almacenan como TEXT con formato ISO corto (yyyy-MM-dd), de modo que
--     las comparaciones lexicograficas y las funciones date()/julianday() de SQLite sean exactas.
--   * Los estados se almacenan con el ORDINAL de los enums de dominio (contrato explicito):
--       invoice_status : 0 Issued  | 1 Partial | 2 Cancelled
--       payment_status : 0 Pending | 1 Overdue | 2 Paid
--   * Este script es idempotente: puede ejecutarse en cada arranque de la aplicacion.
-- =============================================================================================

CREATE TABLE IF NOT EXISTS customers (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    Run          TEXT    NOT NULL,
    Name         TEXT    NOT NULL,
    Email        TEXT    NOT NULL,
    CreatedAtUtc TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS invoices (
    Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
    Number                INTEGER NOT NULL,
    InvoiceDate           TEXT    NOT NULL,
    TotalAmountUnits      INTEGER NOT NULL,
    DaysToDue             INTEGER NOT NULL,
    PaymentDueDate        TEXT    NOT NULL,
    Status                INTEGER NOT NULL DEFAULT 0,
    PaymentStatus         INTEGER NOT NULL DEFAULT 0,
    IsConsistent          INTEGER NOT NULL DEFAULT 1,
    InconsistencyReason   TEXT    NULL,
    DeclaredStatus        INTEGER NULL,
    DeclaredPaymentStatus INTEGER NULL,
    CustomerId            INTEGER NOT NULL,
    CreatedAtUtc          TEXT    NOT NULL,
    UpdatedAtUtc          TEXT    NOT NULL,
    CONSTRAINT fk_invoices_customers FOREIGN KEY (CustomerId) REFERENCES customers (Id) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS invoice_details (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceId     INTEGER NOT NULL,
    ProductName   TEXT    NOT NULL,
    UnitPriceUnits INTEGER NOT NULL,
    Quantity      INTEGER NOT NULL,
    SubtotalUnits INTEGER NOT NULL,
    CONSTRAINT fk_invoice_details_invoices FOREIGN KEY (InvoiceId) REFERENCES invoices (Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS credit_notes (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceId      INTEGER NOT NULL,
    Number         INTEGER NOT NULL,
    CreditNoteDate TEXT    NOT NULL,
    AmountUnits    INTEGER NOT NULL,
    CreatedAtUtc   TEXT    NOT NULL,
    CONSTRAINT fk_credit_notes_invoices FOREIGN KEY (InvoiceId) REFERENCES invoices (Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS invoice_payments (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceId     INTEGER NOT NULL,
    PaymentMethod TEXT    NOT NULL,
    PaymentDate   TEXT    NOT NULL,
    RecordedAtUtc TEXT    NOT NULL,
    CONSTRAINT fk_invoice_payments_invoices FOREIGN KEY (InvoiceId) REFERENCES invoices (Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS import_runs (
    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
    SourceFileName   TEXT    NOT NULL,
    StartedAtUtc     TEXT    NOT NULL,
    FinishedAtUtc    TEXT    NULL,
    TotalRecords     INTEGER NOT NULL DEFAULT 0,
    ImportedRecords  INTEGER NOT NULL DEFAULT 0,
    SkippedRecords   INTEGER NOT NULL DEFAULT 0,
    InconsistentRecords INTEGER NOT NULL DEFAULT 0,
    FailedRecords    INTEGER NOT NULL DEFAULT 0,
    Status           INTEGER NOT NULL DEFAULT 0
);

-- Bitacora de incidencias de cada importacion (tabla hija: permite auditar sin truncar mensajes).
CREATE TABLE IF NOT EXISTS import_run_issues (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    ImportRunId INTEGER NOT NULL,
    Position    INTEGER NOT NULL,
    Message     TEXT    NOT NULL,
    CONSTRAINT fk_import_run_issues_runs FOREIGN KEY (ImportRunId) REFERENCES import_runs (Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS app_users (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    Username       TEXT    NOT NULL,
    DisplayName    TEXT    NOT NULL,
    Role           TEXT    NOT NULL,
    PasswordHash   TEXT    NOT NULL,
    IsActive       INTEGER NOT NULL DEFAULT 1,
    CreatedAtUtc   TEXT    NOT NULL,
    LastLoginAtUtc TEXT    NULL
);

-- Control de version del esquema: guarda la huella de los scripts aplicados. Si la huella cambia y
-- la configuracion lo permite, el inicializador recrea la base (entorno de desarrollo/demo).
CREATE TABLE IF NOT EXISTS schema_migrations (
    Version   TEXT    NOT NULL,
    AppliedAtUtc TEXT NOT NULL
);
