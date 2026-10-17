using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.Ports;

namespace InvoiceHub.Application.UseCases;

/// <summary>
/// Caso de uso: autenticacion de usuarios de la API. La verificacion de la contrasena se delega al
/// puerto <see cref="IPasswordHasher"/> (PBKDF2 en infraestructura) y la emision del token al puerto
/// <see cref="ISecurityTokenIssuer"/>, de modo que este caso de uso no conoce ni el algoritmo de hash
/// ni el formato del token.
/// </summary>
public sealed class LoginHandler
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISecurityTokenIssuer _tokenIssuer;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public LoginHandler(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        ISecurityTokenIssuer tokenIssuer,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _tokenIssuer = tokenIssuer;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<LoginResponseDto> HandleAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var username = request.Username?.Trim();
        var password = request.Password;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            throw new ValidationFailedException(
                "credentials",
                "El usuario y la contrasena son obligatorios.");
        }

        var user = await _users.FindByUsernameAsync(username, cancellationToken);

        // Un unico mensaje para usuario inexistente y contrasena incorrecta: no se revela cual fallo.
        if (user is null || !_passwordHasher.Verify(password, user.PasswordHash))
        {
            throw new AuthenticationFailedException("Usuario o contrasena incorrectos.");
        }

        if (!user.IsActive)
        {
            throw new AuthenticationFailedException("El usuario se encuentra deshabilitado.");
        }

        user.RegisterLogin(_clock.UtcNow);
        await _users.UpdateAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var token = _tokenIssuer.Issue(user);

        return new LoginResponseDto(
            token.Token,
            "Bearer",
            token.ExpiresAtUtc,
            new AuthenticatedUserDto(user.Username, user.DisplayName, user.Role));
    }
}
