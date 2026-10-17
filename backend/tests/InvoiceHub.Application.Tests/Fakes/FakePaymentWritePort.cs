using InvoiceHub.Application.Ports;

namespace InvoiceHub.Application.Tests.Fakes;

/// <summary>
/// Puerto de escritura de pagos falso. El desenlace se inyecta por prueba (pago duplicado, factura
/// cancelada, inconsistente o inexistente) y se registran las solicitudes entregadas al motor.
/// </summary>
internal sealed class FakePaymentWritePort : IPaymentWritePort
{
    public PaymentAppendOutcome PaymentOutcome { get; set; } = PaymentAppendOutcome.Applied;

    public List<PaymentAppendRequest> PaymentRequests { get; } = new();

    public Task<PaymentAppendOutcome> RegisterPaymentAsync(
        PaymentAppendRequest request,
        CancellationToken cancellationToken)
    {
        PaymentRequests.Add(request);
        return Task.FromResult(PaymentOutcome);
    }
}
