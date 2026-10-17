using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Infrastructure.Persistence.Repositories;

/// <summary>
/// Adaptador del repositorio de usuarios. La busqueda por nombre de usuario compara sin distinguir
/// mayusculas (COLATE NOCASE en SQLite) para que el acceso no dependa de como se escribio el usuario al
/// crear la cuenta.
/// </summary>
public sealed class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context) => _context = context;

    public Task<AppUser?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        var normalized = (username ?? string.Empty).Trim();

        // Se retorna la entidad rastreada: el caso de uso actualiza la marca del ultimo acceso.
        return _context.Users
            .FirstOrDefaultAsync(
                user => EF.Functions.Collate(user.Username, "NOCASE") == normalized,
                cancellationToken);
    }

    public async Task AddAsync(AppUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        await _context.Users.AddAsync(user, cancellationToken).ConfigureAwait(false);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken)
        => _context.Users.AsNoTracking().CountAsync(cancellationToken);

    public Task UpdateAsync(AppUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        _context.Users.Update(user);
        return Task.CompletedTask;
    }
}
