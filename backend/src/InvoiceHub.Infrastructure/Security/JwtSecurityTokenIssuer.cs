using System.Globalization;
using System.Security.Claims;
using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Entities;
using InvoiceHub.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace InvoiceHub.Infrastructure.Security;

/// <summary>
/// Emisor de tokens JWT firmados con HMAC-SHA256. La misma configuracion de <see cref="SecurityOptions"/>
/// (emisor, audiencia y clave) es la que valida la API, por lo que ambos extremos comparten el contrato.
/// </summary>
public sealed class JwtSecurityTokenIssuer : ISecurityTokenIssuer
{
    private readonly SecurityOptions _options;
    private readonly IClock _clock;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtSecurityTokenIssuer(IOptions<SecurityOptions> options, IClock clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    public TimeSpan TokenLifetime => _options.TokenLifetime;

    public IssuedToken Issue(AppUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var issuedAt = _clock.UtcNow;
        var expiresAt = issuedAt.Add(TokenLifetime);

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Username),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("display_name", user.DisplayName)
        });

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = identity,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = expiresAt,
            TokenType = "Bearer",
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(_options.ResolveSigningKey()),
                SecurityAlgorithms.HmacSha256)
        };

        return new IssuedToken(_handler.CreateToken(descriptor), expiresAt);
    }
}
