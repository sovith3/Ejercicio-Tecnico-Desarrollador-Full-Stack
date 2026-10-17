using InvoiceHub.Application.Ports;
using InvoiceHub.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace InvoiceHub.Infrastructure.Time;

/// <summary>
/// Reloj del sistema. <see cref="UtcNow"/> es la marca de tiempo tecnica (auditoria) y
/// <see cref="Today"/> la fecha de negocio resuelta en la zona horaria configurada, que es la que se
/// usa para evaluar vencimientos y mora (una factura vence en la fecha de negocio, no en UTC).
/// </summary>
public sealed class SystemClock : IClock
{
    private readonly TimeZoneInfo _timeZone;

    public SystemClock(IOptions<ClockOptions> options)
        => _timeZone = options.Value.ResolveTimeZone();

    public DateTime UtcNow => DateTime.UtcNow;

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _timeZone));
}
