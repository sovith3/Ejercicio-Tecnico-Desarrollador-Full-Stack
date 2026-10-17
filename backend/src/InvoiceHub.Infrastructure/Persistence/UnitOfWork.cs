using InvoiceHub.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Infrastructure.Persistence;

/// <summary>
/// Unidad de trabajo sobre EF Core. Confina el limite transaccional de la aplicacion: los casos de uso
/// preparan los cambios sobre las entidades y este adaptador decide cuando se confirman. La importacion
/// lo usa ademas para liberar el rastreador entre lotes y no acumular el grafo completo en memoria.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;

    public UnitOfWork(AppDbContext context) => _context = context;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => _context.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// Ejecuta una operacion de escritura dentro de una transaccion explicita. Se respeta la estrategia de
    /// ejecucion del proveedor para que la operacion pueda reintentarse de forma segura si la base esta
    /// momentaneamente bloqueada.
    /// </summary>
    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            await operation(cancellationToken).ConfigureAwait(false);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public void ClearChangeTracker() => _context.ChangeTracker.Clear();
}
