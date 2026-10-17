using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Entities;

namespace InvoiceHub.Application.Tests.Fakes;

/// <summary>Repositorio de usuarios falso con busqueda por nombre de usuario ya normalizado.</summary>
internal sealed class FakeUserRepository : IUserRepository
{
    public List<AppUser> Users { get; } = new();

    public List<AppUser> Added { get; } = new();

    public List<AppUser> Updated { get; } = new();

    public Task<AppUser?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
        => Task.FromResult(Users.FirstOrDefault(user =>
            string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase)));

    public Task AddAsync(AppUser user, CancellationToken cancellationToken)
    {
        Added.Add(user);
        Users.Add(user);
        return Task.CompletedTask;
    }

    public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(Users.Count);

    public Task UpdateAsync(AppUser user, CancellationToken cancellationToken)
    {
        Updated.Add(user);
        return Task.CompletedTask;
    }
}

/// <summary>Hasher determinista: el hash es el prefijo <c>hash:</c> mas la contrasena en claro.</summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => $"hash:{password}";

    public bool Verify(string password, string passwordHash) => passwordHash == Hash(password);
}

/// <summary>Emisor de tokens falso que expone la vigencia configurada.</summary>
internal sealed class FakeTokenIssuer : ISecurityTokenIssuer
{
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(8);

    public DateTime IssuedAtUtc { get; set; } = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    public List<string> IssuedUsernames { get; } = new();

    public IssuedToken Issue(AppUser user)
    {
        IssuedUsernames.Add(user.Username);
        return new IssuedToken($"token-{user.Username}-{user.Role}", IssuedAtUtc.Add(TokenLifetime));
    }
}

/// <summary>Unidad de trabajo falsa que solo cuenta los guardados confirmados.</summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCallCount { get; private set; }

    public int TransactionCallCount { get; private set; }

    public int ClearChangeTrackerCallCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCallCount++;
        return Task.FromResult(1);
    }

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        TransactionCallCount++;
        await operation(cancellationToken);
    }

    public void ClearChangeTracker() => ClearChangeTrackerCallCount++;
}
