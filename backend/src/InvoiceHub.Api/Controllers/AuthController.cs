using System.Security.Claims;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceHub.Api.Controllers;

/// <summary>
/// Autenticacion: entrega el token JWT con el que se firman las operaciones de escritura y expone los
/// datos del usuario autenticado. Las contrasenas se verifican contra el hash PBKDF2 almacenado.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
public sealed class AuthController : ControllerBase
{
    private readonly LoginHandler _login;

    public AuthController(LoginHandler login) => _login = login;

    /// <summary>Autentica al usuario y devuelve el token de acceso.</summary>
    /// <param name="request">Usuario y contrasena.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public Task<LoginResponseDto> LoginAsync(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
        => _login.HandleAsync(request, cancellationToken);

    /// <summary>Devuelve los datos contenidos en el token con el que se realizo la solicitud.</summary>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(AuthenticatedUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public ActionResult<AuthenticatedUserDto> Me(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var username = User.FindFirstValue(ClaimTypes.Name)
            ?? User.FindFirstValue("name")
            ?? User.Identity?.Name;

        if (string.IsNullOrWhiteSpace(username))
        {
            // Token valido sin identificacion del usuario: se exige un nuevo inicio de sesion.
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "No autenticado",
                Detail = "El token no identifica a un usuario.",
                Instance = HttpContext.Request.Path
            });
        }

        var displayName = User.FindFirstValue("display_name") ?? username!;
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "User";

        return Ok(new AuthenticatedUserDto(username!, displayName, role));
    }
}
