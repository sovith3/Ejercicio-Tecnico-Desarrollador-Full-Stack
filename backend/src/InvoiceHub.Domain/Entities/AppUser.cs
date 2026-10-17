using InvoiceHub.Domain.Common;

namespace InvoiceHub.Domain.Entities;

/// <summary>Usuario de la API. La autenticacion es opcional segun las bases del examen, pero se
/// incluye para proteger los endpoints de escritura.</summary>
public sealed class AppUser
{
    private AppUser()
    {
        Username = string.Empty;
        DisplayName = string.Empty;
        Role = string.Empty;
        PasswordHash = string.Empty;
    }

    public AppUser(string username, string displayName, string role, string passwordHash, DateTime createdAtUtc)
    {
        Username = Guard.NotEmpty(username, nameof(username)).ToLowerInvariant();
        DisplayName = Guard.NotEmpty(displayName, nameof(displayName));
        Role = Guard.NotEmpty(role, nameof(role));
        PasswordHash = Guard.NotEmpty(passwordHash, nameof(passwordHash));
        CreatedAtUtc = createdAtUtc;
        IsActive = true;
    }

    public long Id { get; private set; }

    public string Username { get; private set; }

    public string DisplayName { get; private set; }

    public string Role { get; private set; }

    public string PasswordHash { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? LastLoginAtUtc { get; private set; }

    public void RegisterLogin(DateTime atUtc) => LastLoginAtUtc = atUtc;

    public void ChangePasswordHash(string passwordHash, DateTime atUtc)
    {
        PasswordHash = Guard.NotEmpty(passwordHash, nameof(passwordHash));
        LastLoginAtUtc = atUtc;
    }

    public void Deactivate() => IsActive = false;
}
