using InvoiceHub.Domain.Entities;

namespace InvoiceHub.Domain.Tests.Entities;

/// <summary>Pruebas de <see cref="AppUser"/>: normalizacion del usuario, registro de acceso y estado.</summary>
public sealed class AppUserTests
{
    private static readonly DateTime CreatedUtc = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Constructor_NormalizesUsernameAndStartsActive()
    {
        var user = new AppUser("  Admin  ", "Administrador", "Administrator", "hash", CreatedUtc);

        Assert.Equal("admin", user.Username);
        Assert.Equal("Administrador", user.DisplayName);
        Assert.Equal("Administrator", user.Role);
        Assert.True(user.IsActive);
        Assert.Null(user.LastLoginAtUtc);
        Assert.Equal(CreatedUtc, user.CreatedAtUtc);
    }

    [Theory]
    [InlineData("", "Administrador", "Administrator", "hash")]
    [InlineData("admin", "   ", "Administrator", "hash")]
    [InlineData("admin", "Administrador", "", "hash")]
    [InlineData("admin", "Administrador", "Administrator", "  ")]
    public void Constructor_WithMissingRequiredData_Throws(
        string username,
        string displayName,
        string role,
        string passwordHash)
    {
        Assert.Throws<ArgumentException>(() => new AppUser(username, displayName, role, passwordHash, CreatedUtc));
    }

    [Fact]
    public void RegisterLogin_StoresLastLoginTimestamp()
    {
        var user = new AppUser("admin", "Administrador", "Administrator", "hash", CreatedUtc);
        var loginUtc = CreatedUtc.AddDays(3);

        user.RegisterLogin(loginUtc);

        Assert.Equal(loginUtc, user.LastLoginAtUtc);
    }

    [Fact]
    public void ChangePasswordHash_UpdatesHashAndLastActivity()
    {
        var user = new AppUser("admin", "Administrador", "Administrator", "hash", CreatedUtc);
        var changeUtc = CreatedUtc.AddDays(5);

        user.ChangePasswordHash("nuevo-hash", changeUtc);

        Assert.Equal("nuevo-hash", user.PasswordHash);
        Assert.Equal(changeUtc, user.LastLoginAtUtc);
    }

    [Fact]
    public void ChangePasswordHash_WithEmptyHash_Throws()
    {
        var user = new AppUser("admin", "Administrador", "Administrator", "hash", CreatedUtc);

        Assert.Throws<ArgumentException>(() => user.ChangePasswordHash("  ", CreatedUtc));
        Assert.Equal("hash", user.PasswordHash);
    }

    [Fact]
    public void Deactivate_DisablesUser()
    {
        var user = new AppUser("admin", "Administrador", "Administrator", "hash", CreatedUtc);

        user.Deactivate();

        Assert.False(user.IsActive);
    }
}
