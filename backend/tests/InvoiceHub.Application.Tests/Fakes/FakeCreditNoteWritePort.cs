using InvoiceHub.Application.Ports;

namespace InvoiceHub.Application.Tests.Fakes;

/// <summary>
/// Puerto de escritura de notas de credito falso. El resultado se inyecta por prueba para reproducir los
/// desenlaces del motor (saldo excedido, numero duplicado, factura cancelada o no encontrada) sin cargar
/// una base real, y se registran las solicitudes para verificar el contrato entregado al motor.
/// </summary>
internal sealed class FakeCreditNoteWritePort : ICreditNoteWritePort
{
    public CreditNoteAppendOutcome CreditNoteOutcome { get; set; } = CreditNoteAppendOutcome.Applied;

    public long? OutstandingBalanceUnits { get; set; }

    public List<CreditNoteAppendRequest> CreditNoteRequests { get; } = new();

    public int GetOutstandingBalanceCallCount { get; private set; }

    public Task<CreditNoteAppendOutcome> AppendCreditNoteAsync(
        CreditNoteAppendRequest request,
        CancellationToken cancellationToken)
    {
        CreditNoteRequests.Add(request);
        return Task.FromResult(CreditNoteOutcome);
    }

    public Task<long?> GetOutstandingBalanceUnitsAsync(long invoiceId, CancellationToken cancellationToken)
    {
        GetOutstandingBalanceCallCount++;
        return Task.FromResult(OutstandingBalanceUnits);
    }
}
