using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.Ports;
using InvoiceHub.Application.ReadModels;

namespace InvoiceHub.Application.Tests.Fakes;

/// <summary>
/// Lector de busqueda falso. Devuelve las filas configuradas por la prueba y registra cuantas veces se
/// consulto el motor, lo que permite verificar tanto la traduccion de resultados como la cache de lectura.
/// <para>
/// <see cref="GetByNumberSequence"/> permite simular el estado antes y despues de una escritura: los casos
/// de uso que emiten notas de credito consultan la fila publicada, escriben y vuelven a consultarla.
/// </para>
/// </summary>
internal sealed class FakeInvoiceSearchReader : IInvoiceSearchReader
{
    private int _getByNumberCalls;

    public List<InvoiceSearchRow> Rows { get; } = new();

    public List<InvoiceSearchRow> GetByNumberSequence { get; } = new();

    public List<InvoiceStatusCountRow> InvoiceStatusCounts { get; } = new();

    public List<PaymentStatusCountRow> PaymentStatusCounts { get; } = new();

    public int SearchCallCount { get; private set; }

    public int GetByNumberCallCount => _getByNumberCalls;

    public bool? LastOnlyConsistent { get; private set; }

    public InvoiceSearchCriteria? LastCriteria { get; private set; }

    /// <summary>Permite forzar el resultado de una busqueda (por ejemplo, filtros aplicados).</summary>
    public Func<InvoiceSearchCriteria, PagedResult<InvoiceSearchRow>>? SearchOverride { get; set; }

    public Task<PagedResult<InvoiceSearchRow>> SearchAsync(
        InvoiceSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        SearchCallCount++;
        LastCriteria = criteria;

        if (SearchOverride is not null)
        {
            return Task.FromResult(SearchOverride(criteria));
        }

        var items = Rows
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToList();

        return Task.FromResult(new PagedResult<InvoiceSearchRow>(items, criteria.Page, criteria.PageSize, Rows.Count));
    }

    public Task<InvoiceSearchRow?> GetByNumberAsync(int number, CancellationToken cancellationToken)
    {
        _getByNumberCalls++;

        if (GetByNumberSequence.Count > 0)
        {
            var index = Math.Min(_getByNumberCalls - 1, GetByNumberSequence.Count - 1);
            return Task.FromResult<InvoiceSearchRow?>(GetByNumberSequence[index]);
        }

        return Task.FromResult(Rows.FirstOrDefault(row => row.Number == number));
    }

    public Task<IReadOnlyList<InvoiceStatusCountRow>> CountByInvoiceStatusAsync(
        bool onlyConsistent,
        CancellationToken cancellationToken)
    {
        LastOnlyConsistent = onlyConsistent;
        return Task.FromResult<IReadOnlyList<InvoiceStatusCountRow>>(InvoiceStatusCounts);
    }

    public Task<IReadOnlyList<PaymentStatusCountRow>> CountByPaymentStatusAsync(
        bool onlyConsistent,
        CancellationToken cancellationToken)
    {
        LastOnlyConsistent = onlyConsistent;
        return Task.FromResult<IReadOnlyList<PaymentStatusCountRow>>(PaymentStatusCounts);
    }
}
