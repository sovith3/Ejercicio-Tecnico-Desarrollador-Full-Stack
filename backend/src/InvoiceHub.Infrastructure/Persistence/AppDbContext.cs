using InvoiceHub.Application.Common;
using InvoiceHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace InvoiceHub.Infrastructure.Persistence;

/// <summary>
/// Contexto de escritura (lado comando del patron CQRS). Mapea las entidades de dominio al esquema
/// creado por los scripts SQL de <c>database/scripts</c>: el esquema es la fuente de verdad y este
/// mapeo solo lo describe para EF Core (no se usa <c>EnsureCreated</c> ni migraciones).
/// </summary>
/// <remarks>
/// Dos convenciones del esquema que el mapeo hace explicitas:
/// <list type="bullet">
/// <item>Los montos viajan como enteros de 1/100000 de peso mediante un <see cref="ValueConverter{TModel,TProvider}"/>;
/// por lo tanto NO deben usarse en clausulas LINQ (no son traducibles): los filtros y agregaciones
/// monetarias se resuelven en los scripts SQL.</item>
/// <item>Los estados se guardan con el ordinal de los enums de dominio, que es un contrato explicito
/// con las rutinas almacenadas y las consultas de reporte.</item>
/// </list>
/// </remarks>
public sealed class AppDbContext : DbContext
{
    private static readonly ValueConverter<decimal, long> MoneyToUnits =
        new(amount => Money.ToUnits(amount), units => Money.FromUnits(units));

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<InvoiceDetail> InvoiceDetails => Set<InvoiceDetail>();

    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();

    public DbSet<InvoicePayment> InvoicePayments => Set<InvoicePayment>();

    public DbSet<ImportRun> ImportRuns => Set<ImportRun>();

    public DbSet<ImportRunIssue> ImportRunIssues => Set<ImportRunIssue>();

    public DbSet<AppUser> Users => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureCustomers(modelBuilder);
        ConfigureInvoices(modelBuilder);
        ConfigureInvoiceDetails(modelBuilder);
        ConfigureCreditNotes(modelBuilder);
        ConfigurePayments(modelBuilder);
        ConfigureImportRuns(modelBuilder);
        ConfigureUsers(modelBuilder);
    }

    private static void ConfigureCustomers(ModelBuilder modelBuilder)
    {
        var customer = modelBuilder.Entity<Customer>();
        customer.ToTable("customers");
        customer.HasKey(entity => entity.Id);
        customer.Property(entity => entity.Id).HasColumnName("Id").ValueGeneratedOnAdd();
        customer.Property(entity => entity.Run).HasColumnName("Run").HasMaxLength(24).IsRequired();
        customer.Property(entity => entity.Name).HasColumnName("Name").HasMaxLength(160).IsRequired();
        customer.Property(entity => entity.Email).HasColumnName("Email").HasMaxLength(180).IsRequired();
        customer.Property(entity => entity.CreatedAtUtc).HasColumnName("CreatedAtUtc").IsRequired();
        customer.HasIndex(entity => entity.Run).IsUnique().HasDatabaseName("ux_customers_run");
        customer
            .HasMany(entity => entity.Invoices)
            .WithOne(invoice => invoice.Customer)
            .HasForeignKey(invoice => invoice.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureInvoices(ModelBuilder modelBuilder)
    {
        var invoice = modelBuilder.Entity<Invoice>();
        invoice.ToTable("invoices");
        invoice.HasKey(entity => entity.Id);
        invoice.Property(entity => entity.Id).HasColumnName("Id").ValueGeneratedOnAdd();
        invoice.Property(entity => entity.Number).HasColumnName("Number").IsRequired();
        invoice.Property(entity => entity.InvoiceDate).HasColumnName("InvoiceDate").IsRequired();
        invoice.Property(entity => entity.TotalAmount).HasColumnName("TotalAmountUnits")
            .HasConversion(MoneyToUnits).IsRequired();
        invoice.Property(entity => entity.DaysToDue).HasColumnName("DaysToDue").IsRequired();
        invoice.Property(entity => entity.PaymentDueDate).HasColumnName("PaymentDueDate").IsRequired();
        invoice.Property(entity => entity.Status).HasColumnName("Status").HasConversion<int>().IsRequired();
        invoice.Property(entity => entity.PaymentStatus).HasColumnName("PaymentStatus").HasConversion<int>().IsRequired();
        invoice.Property(entity => entity.IsConsistent).HasColumnName("IsConsistent").IsRequired();
        invoice.Property(entity => entity.InconsistencyReason).HasColumnName("InconsistencyReason").HasMaxLength(400);
        invoice.Property(entity => entity.DeclaredStatus).HasColumnName("DeclaredStatus").HasConversion<int>();
        invoice.Property(entity => entity.DeclaredPaymentStatus).HasColumnName("DeclaredPaymentStatus").HasConversion<int>();
        invoice.Property(entity => entity.CustomerId).HasColumnName("CustomerId").ValueGeneratedNever().IsRequired();
        invoice.Property(entity => entity.CreatedAtUtc).HasColumnName("CreatedAtUtc").IsRequired();
        invoice.Property(entity => entity.UpdatedAtUtc).HasColumnName("UpdatedAtUtc").IsRequired();
        invoice.HasIndex(entity => entity.Number).IsUnique().HasDatabaseName("ux_invoices_number");
        invoice.HasIndex(entity => entity.PaymentDueDate).HasDatabaseName("ix_invoices_due_date");

        // Colecciones de solo lectura respaldadas por campos privados: EF las materializa escribiendo
        // directamente en _details y _creditNotes.
        invoice
            .HasMany(entity => entity.Details)
            .WithOne(detail => detail.Invoice)
            .HasForeignKey(detail => detail.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
        invoice
            .HasMany(entity => entity.CreditNotes)
            .WithOne(note => note.Invoice)
            .HasForeignKey(note => note.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
        invoice
            .HasOne(entity => entity.Payment)
            .WithOne(payment => payment.Invoice)
            .HasForeignKey<InvoicePayment>(payment => payment.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Propiedades calculadas del dominio: no son columnas. Se ignoran explicitamente para que la
        // forma del modelo coincida exactamente con el esquema creado por los scripts SQL.
        invoice.Ignore(entity => entity.DetailsSubtotalSum);
        invoice.Ignore(entity => entity.CreditNotesTotal);
        invoice.Ignore(entity => entity.OutstandingBalance);
        invoice.Ignore(entity => entity.HasCreditNotes);
        invoice.Ignore(entity => entity.HasPayment);
        invoice.Ignore(entity => entity.HasInconsistentDetailLines);
        invoice.Ignore(entity => entity.IsDeclaredStatusDivergent);
        invoice.Ignore(entity => entity.IsDeclaredPaymentStatusDivergent);
    }

    private static void ConfigureInvoiceDetails(ModelBuilder modelBuilder)
    {
        var detail = modelBuilder.Entity<InvoiceDetail>();
        detail.ToTable("invoice_details");
        detail.HasKey(entity => entity.Id);
        detail.Property(entity => entity.Id).HasColumnName("Id").ValueGeneratedOnAdd();
        detail.Property(entity => entity.InvoiceId).HasColumnName("InvoiceId").ValueGeneratedNever().IsRequired();
        detail.Property(entity => entity.ProductName).HasColumnName("ProductName").IsRequired();
        detail.Property(entity => entity.UnitPrice).HasColumnName("UnitPriceUnits")
            .HasConversion(MoneyToUnits).IsRequired();
        detail.Property(entity => entity.Quantity).HasColumnName("Quantity").IsRequired();
        detail.Property(entity => entity.Subtotal).HasColumnName("SubtotalUnits")
            .HasConversion(MoneyToUnits).IsRequired();
        detail.Ignore(entity => entity.ExpectedSubtotal);
        detail.Ignore(entity => entity.HasInconsistentSubtotal);
    }

    private static void ConfigureCreditNotes(ModelBuilder modelBuilder)
    {
        var note = modelBuilder.Entity<CreditNote>();
        note.ToTable("credit_notes");
        note.HasKey(entity => entity.Id);
        note.Property(entity => entity.Id).HasColumnName("Id").ValueGeneratedOnAdd();
        note.Property(entity => entity.InvoiceId).HasColumnName("InvoiceId").ValueGeneratedNever().IsRequired();
        note.Property(entity => entity.Number).HasColumnName("Number").IsRequired();
        note.Property(entity => entity.CreditNoteDate).HasColumnName("CreditNoteDate").IsRequired();
        note.Property(entity => entity.Amount).HasColumnName("AmountUnits").HasConversion(MoneyToUnits).IsRequired();
        note.Property(entity => entity.CreatedAtUtc).HasColumnName("CreatedAtUtc").IsRequired();
        // El numero de nota de credito es unico en todo el sistema (indice ux_credit_notes_number en
        // 02_schema_indexes.sql): es el identificador natural que valida sp_AppendCreditNote.
        note.HasIndex(entity => entity.Number)
            .IsUnique()
            .HasDatabaseName("ux_credit_notes_number");
    }

    private static void ConfigurePayments(ModelBuilder modelBuilder)
    {
        var payment = modelBuilder.Entity<InvoicePayment>();
        payment.ToTable("invoice_payments");
        payment.HasKey(entity => entity.Id);
        payment.Property(entity => entity.Id).HasColumnName("Id").ValueGeneratedOnAdd();
        payment.Property(entity => entity.InvoiceId).HasColumnName("InvoiceId").ValueGeneratedNever().IsRequired();
        payment.Property(entity => entity.PaymentMethod).HasColumnName("PaymentMethod").IsRequired();
        payment.Property(entity => entity.PaymentDate).HasColumnName("PaymentDate").IsRequired();
        payment.Property(entity => entity.RecordedAtUtc).HasColumnName("RecordedAtUtc").IsRequired();

        // La relacion uno a uno exige unicidad del lado dependiente: una factura tiene a lo sumo un pago.
        payment.HasIndex(entity => entity.InvoiceId).IsUnique().HasDatabaseName("ux_invoice_payments_invoice");
    }

    private static void ConfigureImportRuns(ModelBuilder modelBuilder)
    {
        var run = modelBuilder.Entity<ImportRun>();
        run.ToTable("import_runs");
        run.HasKey(entity => entity.Id);
        run.Property(entity => entity.Id).HasColumnName("Id").ValueGeneratedOnAdd();
        run.Property(entity => entity.SourceFileName).HasColumnName("SourceFileName").IsRequired();
        run.Property(entity => entity.StartedAtUtc).HasColumnName("StartedAtUtc").IsRequired();
        run.Property(entity => entity.FinishedAtUtc).HasColumnName("FinishedAtUtc");
        run.Property(entity => entity.TotalRecords).HasColumnName("TotalRecords").IsRequired();
        run.Property(entity => entity.ImportedRecords).HasColumnName("ImportedRecords").IsRequired();
        run.Property(entity => entity.SkippedRecords).HasColumnName("SkippedRecords").IsRequired();
        run.Property(entity => entity.InconsistentRecords).HasColumnName("InconsistentRecords").IsRequired();
        run.Property(entity => entity.FailedRecords).HasColumnName("FailedRecords").IsRequired();
        run.Property(entity => entity.Status).HasColumnName("Status").HasConversion<int>().IsRequired();
        run.Ignore(entity => entity.Issues);

        // La bitacora se persiste como tabla hija; la coleccion es de solo lectura con campo de respaldo.
        run
            .HasMany(entity => entity.IssueEntries)
            .WithOne(issue => issue.ImportRun)
            .HasForeignKey(issue => issue.ImportRunId)
            .OnDelete(DeleteBehavior.Cascade);

        var issue = modelBuilder.Entity<ImportRunIssue>();
        issue.ToTable("import_run_issues");
        issue.HasKey(entity => entity.Id);
        issue.Property(entity => entity.Id).HasColumnName("Id").ValueGeneratedOnAdd();
        issue.Property(entity => entity.ImportRunId).HasColumnName("ImportRunId").ValueGeneratedNever().IsRequired();
        issue.Property(entity => entity.Position).HasColumnName("Position").IsRequired();
        issue.Property(entity => entity.Message).HasColumnName("Message").IsRequired();
        run.Ignore(entity => entity.DurationMilliseconds);
    }

    private static void ConfigureUsers(ModelBuilder modelBuilder)
    {
        var user = modelBuilder.Entity<AppUser>();
        user.ToTable("app_users");
        user.HasKey(entity => entity.Id);
        user.Property(entity => entity.Id).HasColumnName("Id").ValueGeneratedOnAdd();
        user.Property(entity => entity.Username).HasColumnName("Username").HasMaxLength(80).IsRequired();
        user.Property(entity => entity.DisplayName).HasColumnName("DisplayName").HasMaxLength(160).IsRequired();
        user.Property(entity => entity.Role).HasColumnName("Role").HasMaxLength(60).IsRequired();
        user.Property(entity => entity.PasswordHash).HasColumnName("PasswordHash").IsRequired();
        user.Property(entity => entity.IsActive).HasColumnName("IsActive").IsRequired();
        user.Property(entity => entity.CreatedAtUtc).HasColumnName("CreatedAtUtc").IsRequired();
        user.Property(entity => entity.LastLoginAtUtc).HasColumnName("LastLoginAtUtc");
        // Indice no unico: coincide exactamente con ix_app_users_username en 02_schema_indexes.sql.
        user.HasIndex(entity => entity.Username).HasDatabaseName("ix_app_users_username");
    }
}
