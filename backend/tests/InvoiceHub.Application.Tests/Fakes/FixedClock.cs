using InvoiceHub.Application.Ports;

namespace InvoiceHub.Application.Tests.Fakes;

/// <summary>Reloj fijo para que los casos de uso sean deterministas en las pruebas.</summary>
internal sealed class FixedClock : IClock
{
    public FixedClock(DateOnly today, DateTime utcNow)
    {
        Today = today;
        UtcNow = utcNow;
    }

    public DateTime UtcNow { get; set; }

    public DateOnly Today { get; set; }

    /// <summary>Reloj por omision: 15 de enero de 2026 (fecha de negocio de las pruebas).</summary>
    public static FixedClock Default()
        => new(new DateOnly(2026, 1, 15), new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc));
}
