using Dapper;
using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.Ports;
using InvoiceHub.Application.ReadModels;

namespace InvoiceHub.Infrastructure.Persistence.Readers;

/// <summary>
/// Adaptador de lectura de facturas. Todo el trabajo (join con cliente, agregacion de notas de credito,
/// pago registrado, saldo pendiente, filtros, orden y paginado) ocurre dentro del motor sobre la vista
/// <c>vw_invoice_search</c>; la aplicacion solo transporta los criterios ya normalizados.
/// </summary>
public sealed class InvoiceSearchReader : IInvoiceSearchReader
{
    private readonly SqliteConnectionFactory _connections;
    private readonly SqlScriptCatalog _scripts;
    private readonly IClock _clock;

    public InvoiceSearchReader(SqliteConnectionFactory connections, SqlScriptCatalog scripts, IClock clock)
    {
        _connections = connections;
        _scripts = scripts;
        _clock = clock;
    }

    public async Task<PagedResult<InvoiceSearchRow>> SearchAsync(
        InvoiceSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        InvoiceSearchCriteriaNormalizer.Normalize(criteria);

        var parameters = BuildParameters(criteria, includePaging: true);

        await using var connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = (await connection.QueryAsync<InvoiceSearchRow>(
            new CommandDefinition(_scripts.Read(SqlScriptCatalog.InvoiceSearch), parameters, cancellationToken: cancellationToken))
            .ConfigureAwait(false)).AsList();

        var total = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(_scripts.Read(SqlScriptCatalog.InvoiceSearchCount), parameters, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return new PagedResult<InvoiceSearchRow>(rows, criteria.Page, criteria.PageSize, total);
    }

    /// <summary>
    /// Busca una factura por su numero reutilizando la misma consulta de busqueda. Se fija
    /// <c>OnlyActiveSystem = 0</c> adrede: tambien las facturas fuera del sistema activo deben poder
    /// consultarse, ya que son justamente las que se auditan en la interfaz.
    /// </summary>
    public async Task<InvoiceSearchRow?> GetByNumberAsync(int number, CancellationToken cancellationToken)
    {
        var criteria = new InvoiceSearchCriteria
        {
            Number = number,
            Page = 1,
            PageSize = 1,
            SortBy = InvoiceSearchCriteriaNormalizer.DefaultSortBy,
            SortDirection = InvoiceSearchCriteriaNormalizer.DefaultSortDirection
        };

        var page = await SearchAsync(criteria, cancellationToken).ConfigureAwait(false);
        return page.Items.Count > 0 ? page.Items[0] : null;
    }

    public Task<IReadOnlyList<InvoiceStatusCountRow>> CountByInvoiceStatusAsync(
        bool onlyConsistent,
        CancellationToken cancellationToken)
        => CountAsync<InvoiceStatusCountRow>(SqlScriptCatalog.InvoiceStatusCounts, onlyConsistent, cancellationToken);

    public Task<IReadOnlyList<PaymentStatusCountRow>> CountByPaymentStatusAsync(
        bool onlyConsistent,
        CancellationToken cancellationToken)
        => CountAsync<PaymentStatusCountRow>(SqlScriptCatalog.PaymentStatusCounts, onlyConsistent, cancellationToken);

    private async Task<IReadOnlyList<TRow>> CountAsync<TRow>(
        string script,
        bool onlyConsistent,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await connection.QueryAsync<TRow>(
            new CommandDefinition(
                _scripts.Read(script),
                new { OnlyConsistent = onlyConsistent ? 1 : 0 },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return rows.AsList();
    }

    /// <summary>
    /// Parametros de las consultas 20 y 21 (comprometen exactamente los mismos nombres). Los filtros
    /// opcionales viajan como <c>null</c> para que la clausula WHERE los ignore, y la fecha de negocio se
    /// envia en el formato de almacenamiento del esquema (TEXT ISO corto).
    /// </summary>
    private Dictionary<string, object?> BuildParameters(InvoiceSearchCriteria criteria, bool includePaging)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Number"] = criteria.Number,
            ["InvoiceStatus"] = criteria.InvoiceStatus is null ? null : (int)criteria.InvoiceStatus.Value,
            ["PaymentStatus"] = criteria.PaymentStatus is null ? null : (int)criteria.PaymentStatus.Value,
            ["IsConsistent"] = criteria.IsConsistent is null ? null : criteria.IsConsistent.Value ? 1 : 0,
            ["OnlyActiveSystem"] = criteria.OnlyActiveSystem ? 1 : 0,
            ["CustomerRunLike"] = ToLikePattern(criteria.CustomerRun),
            ["TextLike"] = ToLikePattern(criteria.Text),
            ["MinDaysOverdue"] = criteria.MinDaysOverdue,
            ["Today"] = SqliteValueFormat.ToStorage(_clock.Today),
            ["SortBy"] = criteria.SortBy,
            ["SortDirection"] = criteria.SortDirection
        };

        if (includePaging)
        {
            parameters["PageSize"] = criteria.PageSize;
            parameters["Offset"] = (criteria.Page - 1) * criteria.PageSize;
        }

        return parameters;
    }

    private static string? ToLikePattern(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : $"%{value.Trim()}%";
}
