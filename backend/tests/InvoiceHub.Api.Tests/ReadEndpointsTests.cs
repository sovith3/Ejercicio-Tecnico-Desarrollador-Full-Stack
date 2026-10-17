using System.Net;
using InvoiceHub.Api.Tests.Support;

namespace InvoiceHub.Api.Tests;

/// <summary>
/// Pruebas de integracion de los endpoints de lectura: arranque completo de la API sobre una base SQLite
/// temporal con la importacion del archivo del enunciado, informacion de descubrimiento, salud,
/// diagnostico, busqueda, detalle, catalogo de estados y los tres reportes exigidos.
/// </summary>
public sealed class ReadEndpointsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ReadEndpointsTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Root_DescribesTheApiAndItsEndpoints()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var info = await ApiFactory.GetJsonAsync(response);
        Assert.Equal("InvoiceHub API", info.GetProperty("name").GetString());
        Assert.Equal("1.0.0", info.GetProperty("version").GetString());
        Assert.Equal("Testing", info.GetProperty("environment").GetString());
        Assert.Equal(
            "GET /api/invoices",
            info.GetProperty("endpoints").GetProperty("searchInvoices").GetString());
    }

    [Fact]
    public async Task Health_ReportsEveryCheckAsJson()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var health = await ApiFactory.GetJsonAsync(response);
        Assert.NotEqual("Unhealthy", health.GetProperty("status").GetString());
        Assert.True(health.GetProperty("checks").GetArrayLength() > 0);
        Assert.All(
            health.GetProperty("checks").EnumerateArray(),
            check => Assert.Contains(
                check.GetProperty("status").GetString(),
                new[] { "Healthy", "Degraded", "Unhealthy" }));
    }

    [Fact]
    public async Task Liveness_IsAlwaysHealthy()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", (await ApiFactory.GetJsonAsync(response)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Diagnostics_ReportsSchemaCacheAndImportedRows()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/diagnostics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var diagnostics = await ApiFactory.GetJsonAsync(response);

        Assert.Equal("Testing", diagnostics.GetProperty("environment").GetString());
        Assert.StartsWith("schema-", diagnostics.GetProperty("database").GetProperty("schemaFingerprint").GetString());
        Assert.Equal(
            ApiFactory.ImportedInvoiceCount,
            diagnostics.GetProperty("database").GetProperty("invoiceCount").GetInt32());
        Assert.Equal("memory", diagnostics.GetProperty("cache").GetProperty("provider").GetString());
        Assert.True(diagnostics.GetProperty("sourceFile").GetProperty("exists").GetBoolean());
        Assert.Equal(
            ApiFactory.ImportedInvoiceCount,
            diagnostics.GetProperty("lastImport").GetProperty("importedRecords").GetInt32());
    }

    [Fact]
    public async Task SearchInvoices_ReturnsTheImportedUniverseWithDomainStatusesAndCalculatedBalance()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/invoices?page=1&pageSize=10&sortBy=dueDate&sortDirection=desc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ApiFactory.GetJsonAsync(response);

        Assert.Equal(10, page.GetProperty("items").GetArrayLength());
        Assert.Equal(ApiFactory.ActiveInvoiceCount, page.GetProperty("totalCount").GetInt32());
        Assert.True(page.GetProperty("hasNextPage").GetBoolean());

        foreach (var item in page.GetProperty("items").EnumerateArray())
        {
            Assert.Contains(item.GetProperty("invoiceStatus").GetString(), new[] { "Issued", "Partial", "Cancelled" });
            Assert.Contains(item.GetProperty("paymentStatus").GetString(), new[] { "Pending", "Overdue", "Paid" });
            Assert.True(item.GetProperty("totalAmount").GetDecimal() > 0m);
            Assert.Equal(
                item.GetProperty("totalAmount").GetDecimal() - item.GetProperty("creditNotesTotal").GetDecimal(),
                item.GetProperty("outstandingBalance").GetDecimal());
        }
    }

    [Fact]
    public async Task SearchInvoices_SortedByDueDateDescending_ReturnsDueDateOrder()
    {
        using var client = _factory.CreateClient();

        var page = await ApiFactory.GetJsonAsync(
            client,
            "/api/invoices?pageSize=100&sortBy=dueDate&sortDirection=desc");

        var dueDates = page.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("paymentDueDate").GetDateTime())
            .ToList();

        Assert.Equal(dueDates.OrderByDescending(date => date), dueDates);
    }

    [Fact]
    public async Task SearchInvoices_ReturnsOnlyTheActiveSystem()
    {
        using var client = _factory.CreateClient();

        var page = await ApiFactory.GetJsonAsync(client, "/api/invoices?pageSize=200");

        Assert.Equal(ApiFactory.ActiveInvoiceCount, page.GetProperty("totalCount").GetInt32());
        Assert.All(
            page.GetProperty("items").EnumerateArray(),
            item => Assert.True(item.GetProperty("isConsistent").GetBoolean()));
    }

    [Fact]
    public async Task SearchInvoices_WithInvalidPageSize_ReturnsValidationProblem()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/invoices?pageSize=1000");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ApiFactory.GetJsonAsync(response);
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        // El nombre del campo del modelo llega tal como se declara en el criterio ("PageSize").
        Assert.Contains(
            problem.GetProperty("errors").EnumerateObject().Select(property => property.Name),
            name => string.Equals(name, "pageSize", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetInvoiceDetail_ForAnImportedInvoice_ReturnsCalculatedAndDeclaredStatuses()
    {
        using var client = _factory.CreateClient();

        var number = await ApiFactory.FirstInvoiceNumberAsync(client);

        var response = await client.GetAsync($"/api/invoices/{number}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = await ApiFactory.GetJsonAsync(response);

        Assert.Equal(number, detail.GetProperty("number").GetInt32());
        Assert.Equal(
            detail.GetProperty("totalAmount").GetDecimal(),
            detail.GetProperty("detailsSubtotalSum").GetDecimal());
        Assert.True(detail.GetProperty("details").GetArrayLength() > 0);
        Assert.False(string.IsNullOrWhiteSpace(detail.GetProperty("customerRun").GetString()));
        Assert.Equal("Issued", detail.GetProperty("invoiceStatus").GetString());
    }

    [Fact]
    public async Task GetInvoiceDetail_WithUnknownNumber_ReturnsNotFoundProblem()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/invoices/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await ApiFactory.GetJsonAsync(response);
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.Contains("999999", problem.GetProperty("detail").GetString()!);
    }

    [Fact]
    public async Task StatusCatalog_ReturnsEveryStatusWithCountsAndAmounts()
    {
        using var client = _factory.CreateClient();

        var catalog = await ApiFactory.GetJsonAsync(client, "/api/invoices/status-catalog");

        Assert.Equal(3, catalog.GetProperty("invoiceStatuses").GetArrayLength());
        Assert.Equal(3, catalog.GetProperty("paymentStatuses").GetArrayLength());

        var invoiceCounts = catalog.GetProperty("invoiceStatuses").EnumerateArray()
            .Select(option => option.GetProperty("invoiceCount").GetInt32())
            .Sum();

        Assert.Equal(ApiFactory.ImportedInvoiceCount, invoiceCounts);
        Assert.All(
            catalog.GetProperty("invoiceStatuses").EnumerateArray(),
            option => Assert.False(string.IsNullOrWhiteSpace(option.GetProperty("label").GetString())));
    }

    [Fact]
    public async Task Reports_ReturnTheThreeRequiredReportsAndTheDashboard()
    {
        using var client = _factory.CreateClient();

        var summary = await client.GetAsync("/api/reports/payment-status-summary");
        var overdue = await client.GetAsync("/api/reports/overdue-without-activity?minDaysOverdue=30");
        var inconsistent = await client.GetAsync("/api/reports/inconsistent-invoices");
        var dashboard = await client.GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        Assert.Equal(HttpStatusCode.OK, overdue.StatusCode);
        Assert.Equal(HttpStatusCode.OK, inconsistent.StatusCode);
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);

        var summaryBody = await ApiFactory.GetJsonAsync(summary);
        Assert.Equal(ApiFactory.ActiveInvoiceCount, summaryBody.GetProperty("totalInvoices").GetInt32());
        Assert.Equal(
            summaryBody.GetProperty("totalInvoices").GetInt32(),
            summaryBody.GetProperty("items").EnumerateArray().Sum(item => item.GetProperty("invoiceCount").GetInt32()));

        var overdueBody = await ApiFactory.GetJsonAsync(overdue);
        Assert.Equal(30, overdueBody.GetProperty("minDaysOverdue").GetInt32());
        Assert.Equal(
            overdueBody.GetProperty("items").GetArrayLength(),
            overdueBody.GetProperty("totalInvoices").GetInt32());

        var inconsistentBody = await ApiFactory.GetJsonAsync(inconsistent);
        Assert.True(inconsistentBody.GetProperty("totalInvoices").GetInt32() >= 0);

        var dashboardBody = await ApiFactory.GetJsonAsync(dashboard);
        Assert.Equal(ApiFactory.ImportedInvoiceCount, dashboardBody.GetProperty("totalInvoices").GetInt32());
        Assert.True(dashboardBody.GetProperty("monthlyBilling").GetArrayLength() > 0);
        Assert.Equal(
            dashboardBody.GetProperty("activeInvoices").GetInt32(),
            dashboardBody.GetProperty("byInvoiceStatus").EnumerateArray()
                .Sum(item => item.GetProperty("invoiceCount").GetInt32()));
    }

    [Fact]
    public async Task Reports_WithInvalidThreshold_ReturnValidationProblem()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/reports/overdue-without-activity?minDaysOverdue=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRoute_ReturnsNotFoundProblem()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
