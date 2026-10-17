using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InvoiceHub.Api.Tests.Support;

namespace InvoiceHub.Api.Tests;

/// <summary>
/// Pruebas de integracion del flujo de escritura: autenticacion JWT, emision de notas de credito y registro
/// de pagos. Verifican el codigo de estado, la cabecera <c>Location</c> del recurso creado, la invalidacion
/// de la cache de lectura y la traduccion de las reglas de negocio a 422/409.
/// </summary>
public sealed class WriteEndpointsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public WriteEndpointsTests(ApiFactory factory) => _factory = factory;

    private static async Task<JsonElement> ItemWithoutPaymentAsync(HttpClient client)
    {
        var page = await ApiFactory.GetJsonAsync(client, "/api/invoices?pageSize=100");

        return page.GetProperty("items").EnumerateArray()
            .First(item => !item.GetProperty("hasPayment").GetBoolean());
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorizedProblem()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = ApiFactory.AdminUsername, password = "clave-incorrecta" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CurrentUser_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_ThenCurrentUser_ReturnsTheAuthenticatedUser()
    {
        using var client = _factory.CreateClient();

        var token = await ApiFactory.LoginAsync(client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var user = await ApiFactory.GetJsonAsync(response);
        Assert.Equal(ApiFactory.AdminUsername, user.GetProperty("username").GetString());
        Assert.Equal("Administrator", user.GetProperty("role").GetString());
    }

    [Fact]
    public async Task AddCreditNote_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var item = await ItemWithoutPaymentAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/invoices/{item.GetProperty("number").GetInt32()}/credit-notes",
            new { amount = 10m });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AddCreditNote_WithToken_CreatesTheNoteUpdatesTheInvoiceAndInvalidatesTheCache()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await ApiFactory.LoginAsync(client));

        var item = await ItemWithoutPaymentAsync(client);
        var number = item.GetProperty("number").GetInt32();
        var total = item.GetProperty("totalAmount").GetDecimal();
        var amount = decimal.Round(total / 4m, 2);

        var response = await client.PostAsJsonAsync(
            $"/api/invoices/{number}/credit-notes",
            new { amount, creditNoteDate = "2025-06-30" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.EndsWith($"/api/invoices/{number}", response.Headers.Location!.ToString());

        var body = await ApiFactory.GetJsonAsync(response);
        Assert.Equal(number, body.GetProperty("invoiceNumber").GetInt32());
        Assert.Equal(amount, body.GetProperty("creditNoteAmount").GetDecimal());
        Assert.Equal(amount, body.GetProperty("creditNotesTotal").GetDecimal());
        Assert.Equal(total - amount, body.GetProperty("outstandingBalance").GetDecimal());
        Assert.Equal("Partial", body.GetProperty("invoiceStatus").GetString());
        Assert.True(body.GetProperty("creditNoteNumber").GetInt32() > 0);

        // La escritura invalida la cache de lectura: el detalle ya refleja la nota recien creada.
        var detail = await ApiFactory.GetJsonAsync(client, $"/api/invoices/{number}");
        Assert.Equal("Partial", detail.GetProperty("invoiceStatus").GetString());
        Assert.Equal(amount, detail.GetProperty("creditNotesTotal").GetDecimal());
        Assert.Equal(1, detail.GetProperty("creditNotes").GetArrayLength());
    }

    [Fact]
    public async Task AddCreditNote_ExceedingTheOutstandingBalance_ReturnsUnprocessableEntity()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await ApiFactory.LoginAsync(client));

        var item = await ItemWithoutPaymentAsync(client);
        var number = item.GetProperty("number").GetInt32();
        var outstanding = item.GetProperty("outstandingBalance").GetDecimal();

        var response = await client.PostAsJsonAsync(
            $"/api/invoices/{number}/credit-notes",
            new { amount = outstanding + 1000m });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var problem = await ApiFactory.GetJsonAsync(response);
        Assert.Equal("CreditNoteExceedsOutstandingBalance", problem.GetProperty("businessCode").GetString());
        Assert.Equal(422, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task AddCreditNote_WithZeroAmount_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await ApiFactory.LoginAsync(client));

        var item = await ItemWithoutPaymentAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/invoices/{item.GetProperty("number").GetInt32()}/credit-notes",
            new { amount = 0m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddCreditNote_ForAnUnknownInvoice_ReturnsNotFound()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await ApiFactory.LoginAsync(client));

        var response = await client.PostAsJsonAsync(
            "/api/invoices/999999/credit-notes",
            new { amount = 100m });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RegisterPayment_WithToken_MarksTheInvoicePaidAndRejectsTheSecondAttempt()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await ApiFactory.LoginAsync(client));

        var item = await ItemWithoutPaymentAsync(client);
        var number = item.GetProperty("number").GetInt32();

        var first = await client.PostAsJsonAsync(
            $"/api/invoices/{number}/payments",
            new { paymentMethod = "Transferencia", paymentDate = "2025-06-30" });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var detail = await ApiFactory.GetJsonAsync(first);
        Assert.Equal("Paid", detail.GetProperty("paymentStatus").GetString());
        Assert.Equal("Transferencia", detail.GetProperty("payment")!.GetProperty("paymentMethod").GetString());
        Assert.Equal("2025-06-30", detail.GetProperty("payment").GetProperty("paymentDate").GetString());

        var second = await client.PostAsJsonAsync(
            $"/api/invoices/{number}/payments",
            new { paymentMethod = "Efectivo" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var problem = await ApiFactory.GetJsonAsync(second);
        Assert.Equal(409, problem.GetProperty("status").GetInt32());
        Assert.Contains("pago registrado", problem.GetProperty("detail").GetString()!);
    }

    [Fact]
    public async Task RegisterPayment_WithoutMethod_ReturnsValidationProblem()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await ApiFactory.LoginAsync(client));

        var item = await ItemWithoutPaymentAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/invoices/{item.GetProperty("number").GetInt32()}/payments",
            new { paymentMethod = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ApiFactory.GetJsonAsync(response);
        Assert.Contains(
            problem.GetProperty("errors").EnumerateObject().Select(p => p.Name),
            name => string.Equals(name, "paymentMethod", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RegisterPayment_ForAnUnknownInvoice_ReturnsNotFound()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await ApiFactory.LoginAsync(client));

        var response = await client.PostAsJsonAsync(
            "/api/invoices/999999/payments",
            new { paymentMethod = "Transferencia" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Import_SourceStatus_DescribesTheFileAndTheLastRun()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await ApiFactory.LoginAsync(client));

        var response = await client.GetAsync("/api/imports/source");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ApiFactory.GetJsonAsync(response);
        Assert.True(body.GetProperty("exists").GetBoolean());
        Assert.Equal(ApiFactory.ImportedInvoiceCount, body.GetProperty("persistedInvoices").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("lastRun").GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Import_RepeatedRun_IsIdempotentAndKeepsTheInvoiceCount()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await ApiFactory.LoginAsync(client));

        var response = await client.PostAsync("/api/imports", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await ApiFactory.GetJsonAsync(response);
        Assert.Equal(ApiFactory.ImportedInvoiceCount, summary.GetProperty("totalRecords").GetInt32());
        Assert.Equal(ApiFactory.ImportedInvoiceCount, summary.GetProperty("skipped").GetInt32());
        Assert.Equal(0, summary.GetProperty("failed").GetInt32());

        var page = await ApiFactory.GetJsonAsync(client, "/api/invoices?pageSize=200");
        Assert.Equal(ApiFactory.ActiveInvoiceCount, page.GetProperty("totalCount").GetInt32());
    }
}
