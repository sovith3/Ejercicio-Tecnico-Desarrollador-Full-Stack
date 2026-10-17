using InvoiceHub.Domain.Entities;

namespace InvoiceHub.Application.Ports;

/// <summary>Puerto de salida para el repositorio de usuarios.</summary>
public interface IUserRepository
{
    Task<AppUser?> FindByUsernameAsync(string username, CancellationToken cancellationToken);

    Task AddAsync(AppUser user, CancellationToken cancellationToken);

    Task<int> CountAsync(CancellationToken cancellationToken);

    Task UpdateAsync(AppUser user, CancellationToken cancellationToken);
}

/// <summary>Puerto de derivacion y verificacion de hash de contrasenas (PBKDF2 en infraestructura).</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string passwordHash);
}

/// <summary>Puerto de emision de tokens de acceso (JWT en infraestructura).</summary>
public interface ISecurityTokenIssuer
{
    IssuedToken Issue(AppUser user);

    TimeSpan TokenLifetime { get; }
}

/// <summary>Token emitido y su vigencia.</summary>
public sealed record IssuedToken(string Token, DateTime ExpiresAtUtc);
