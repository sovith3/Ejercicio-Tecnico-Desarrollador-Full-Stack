using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.Tests.Fakes;
using InvoiceHub.Application.UseCases;
using InvoiceHub.Domain.Entities;

namespace InvoiceHub.Application.Tests.UseCases;

/// <summary>
/// Pruebas del caso de uso de autenticacion: mensaje unico ante credenciales invalidas (no se revela si
/// el usuario existe), rechazo de usuarios deshabilitados y registro del ultimo acceso al emitir el token.
/// </summary>
public sealed class LoginHandlerTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly FakeTokenIssuer _tokenIssuer = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FixedClock _clock = FixedClock.Default();

    private AppUser Admin(string username = "admin", string password = "secreto", bool isActive = true)
    {
        var user = new AppUser(username, "Administrador InvoiceHub", "Administrator", _hasher.Hash(password), _clock.UtcNow);
        if (!isActive)
        {
            user.Deactivate();
        }

        return user;
    }

    private LoginHandler CreateHandler()
        => new(_users, _hasher, _tokenIssuer, _unitOfWork, _clock);

    [Fact]
    public async Task HandleAsync_WithValidCredentials_ReturnsTokenAndRegistersLogin()
    {
        _users.Users.Add(Admin());

        var response = await CreateHandler().HandleAsync(
            new LoginRequest { Username = "  admin  ", Password = "secreto" },
            CancellationToken.None);

        Assert.Equal("Bearer", response.TokenType);
        Assert.Equal("token-admin-Administrator", response.AccessToken);
        Assert.Equal(_clock.UtcNow.Add(_tokenIssuer.TokenLifetime), response.ExpiresAtUtc);
        Assert.Equal("admin", response.User.Username);
        Assert.Equal("Administrador InvoiceHub", response.User.DisplayName);
        Assert.Equal("Administrator", response.User.Role);

        Assert.Equal(new[] { "admin" }, _tokenIssuer.IssuedUsernames);
        Assert.Equal(_clock.UtcNow, Assert.Single(_users.Users).LastLoginAtUtc);
        Assert.Single(_users.Updated);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleAsync_WithUserNameInDifferentCase_FindsTheUser()
    {
        _users.Users.Add(Admin(username: "Administrador"));

        var response = await CreateHandler().HandleAsync(
            new LoginRequest { Username = "administrador", Password = "secreto" },
            CancellationToken.None);

        Assert.Equal("administrador", response.User.Username);
    }

    [Fact]
    public async Task HandleAsync_WhenUserDoesNotExist_ThrowsAuthenticationFailed()
    {
        var exception = await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            CreateHandler().HandleAsync(
                new LoginRequest { Username = "fantasma", Password = "secreto" },
                CancellationToken.None));

        Assert.Equal("Usuario o contrasena incorrectos.", exception.Message);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_tokenIssuer.IssuedUsernames);
    }

    [Fact]
    public async Task HandleAsync_WithWrongPassword_ThrowsSameMessageAsUnknownUser()
    {
        _users.Users.Add(Admin());

        var exception = await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            CreateHandler().HandleAsync(
                new LoginRequest { Username = "admin", Password = "incorrecta" },
                CancellationToken.None));

        Assert.Equal("Usuario o contrasena incorrectos.", exception.Message);
        Assert.Null(Assert.Single(_users.Users).LastLoginAtUtc);
    }

    [Fact]
    public async Task HandleAsync_WhenUserIsDisabled_ThrowsAuthenticationFailed()
    {
        _users.Users.Add(Admin(isActive: false));

        var exception = await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            CreateHandler().HandleAsync(
                new LoginRequest { Username = "admin", Password = "secreto" },
                CancellationToken.None));

        Assert.Equal("El usuario se encuentra deshabilitado.", exception.Message);
        Assert.Empty(_tokenIssuer.IssuedUsernames);
    }

    [Theory]
    [InlineData(null, "secreto")]
    [InlineData("   ", "secreto")]
    [InlineData("admin", null)]
    [InlineData("admin", "")]
    public async Task HandleAsync_WithoutCredentials_ThrowsValidationFailed(string? username, string? password)
    {
        var exception = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            CreateHandler().HandleAsync(
                new LoginRequest { Username = username, Password = password },
                CancellationToken.None));

        Assert.Contains("credentials", exception.Errors.Keys);
    }

    [Fact]
    public async Task HandleAsync_WithNullRequest_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            CreateHandler().HandleAsync(null!, CancellationToken.None));
    }
}
