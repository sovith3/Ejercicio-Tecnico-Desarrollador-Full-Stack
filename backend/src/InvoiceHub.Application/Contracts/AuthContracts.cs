using System.ComponentModel.DataAnnotations;

namespace InvoiceHub.Application.Contracts;

/// <summary>Credenciales de acceso.</summary>
public sealed class LoginRequest
{
    [Required(ErrorMessage = "El usuario es obligatorio.")]
    [MaxLength(64, ErrorMessage = "El usuario no puede superar los 64 caracteres.")]
    public string? Username { get; set; }

    [Required(ErrorMessage = "La contrasena es obligatoria.")]
    [MaxLength(128, ErrorMessage = "La contrasena no puede superar los 128 caracteres.")]
    public string? Password { get; set; }
}

/// <summary>Usuario autenticado.</summary>
public sealed record AuthenticatedUserDto(string Username, string DisplayName, string Role);

/// <summary>Respuesta de autenticacion con el token de acceso.</summary>
public sealed record LoginResponseDto(string AccessToken, string TokenType, DateTime ExpiresAtUtc, AuthenticatedUserDto User);
