using InvoiceHub.Application.Ports;

namespace InvoiceHub.Application.Tests.Fakes;

/// <summary>
/// Puerto de recalculo de estados falso. Se contabilizan las invocaciones (universo completo o factura
/// unica) para verificar que cada caso de uso recalcule unicamente lo que necesita, sin cargar una base.
/// </summary>
internal sealed class FakeStatusRecalculationPort : IStatusRecalculationPort
{
    public int RecalculateAllStatusesResult { get; set; } = 1;

    public List<long> RecalculatedInvoiceIds { get; } = new();

    public int RecalculateAllCallCount { get; private set; }

    public Task<int> RecalculateAllStatusesAsync(CancellationToken cancellationToken)
    {
        RecalculateAllCallCount++;
        return Task.FromResult(RecalculateAllStatusesResult);
    }

    public Task<int> RecalculateInvoiceStatusesAsync(long invoiceId, CancellationToken cancellationToken)
    {
        RecalculatedInvoiceIds.Add(invoiceId);
        return Task.FromResult(1);
    }
}
