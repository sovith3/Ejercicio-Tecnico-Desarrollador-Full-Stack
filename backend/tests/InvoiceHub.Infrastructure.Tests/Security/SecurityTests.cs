using System.Security.Claims;
using InvoiceHub.Application.Ports;
using InvoiceHub.Domain.Entities;
using InvoiceHub.Infrastructure.Configuration;
using InvoiceHub.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace InvoiceHub.Infrastructure.Tests.Security;

/// <summary>
/// Pruebas de los adaptadores de seguridad: derivacion PBKDF2 de contrasenas y emision/validacion del token
/// JWT con la misma configuracion (emisor, audiencia y clave) que usa la API para autenticar.
/// </summary>
public sealed class SecurityTests
{
    private static readonly DateTime IssuedAtUtc = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private static SecurityOptions TestOptions() => new()
    {
        Issuer = "InvoiceHub.Tests",
        Audience = "InvoiceHub.Tests.Client",
        Key = "clave-de-firma-para-pruebas-automatizadas-2026",
        TokenLifetimeMinutes = 120
    };

    private static AppUser User(string username = "admin", string role = "Administrator")
        => new(username, "Administrador InvoiceHub", role, "hash", IssuedAtUtc);

    [Fact]
    public void Hash_ProducesVerifiableHashedPasswordWithItsOwnSalt()
    {
        var hasher = new Pbkdf2PasswordHasher();

        var first = hasher.Hash("Clave.Secreta.2026");
        var second = hasher.Hash("Clave.Secreta.2026");

        Assert.NotEqual(first, second);
        Assert.StartsWith("pbkdf2-sha256$", first);
        Assert.Equal(4, first.Split('$').Length);
        Assert.DoesNotContain("Clave.Secreta.2026", first);
        Assert.True(hasher.Verify("Clave.Secreta.2026", first));
        Assert.True(hasher.Verify("Clave.Secreta.2026", second));
        Assert.False(hasher.Verify("Clave.Secreta.2027", first));
    }

    [Fact]
    public void Hash_WithNullPassword_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Pbkdf2PasswordHasher().Hash(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("texto-plano")]
    [InlineData("pbkdf2-sha256$0$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha256$100000$sal-invalida!$aGFzaA==")]
    [InlineData("pbkdf2-sha256$100000$c2FsdA==")]
    public void Verify_WithMalformedOrForeignHash_ReturnsFalse(string passwordHash)
    {
        var hasher = new Pbkdf2PasswordHasher();

        Assert.False(hasher.Verify("Clave.Secreta.2026", passwordHash));
    }

    [Fact]
    public void Verify_WithEmptyPassword_ReturnsFalse()
    {
        var hasher = new Pbkdf2PasswordHasher();

        Assert.False(hasher.Verify(string.Empty, hasher.Hash("Clave.Secreta.2026")));
    }

    [Fact]
    public async Task Issue_ProducesATokenThatValidatesWithTheSameConfiguration()
    {
        var options = TestOptions();
        var issuer = new JwtSecurityTokenIssuer(Options.Create(options), new StubClock(IssuedAtUtc));

        var issued = issuer.Issue(User());

        Assert.Equal(options.TokenLifetime, issuer.TokenLifetime);
        Assert.Equal(IssuedAtUtc.Add(options.TokenLifetime), issued.ExpiresAtUtc);
        Assert.NotEmpty(issued.Token);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(issued.Token, ValidationParameters(options));

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal("admin", result.ClaimsIdentity.FindFirst("sub")?.Value);
        Assert.Equal("Administrator", result.ClaimsIdentity.FindFirst(ClaimTypes.Role)?.Value);
        Assert.Equal("Administrador InvoiceHub", result.ClaimsIdentity.FindFirst("display_name")?.Value);
        Assert.Equal(options.Issuer, result.Issuer);
    }

    [Fact]
    public async Task Issue_WithAnotherSigningKey_ProducesATokenThatFailsValidation()
    {
        var issuer = new JwtSecurityTokenIssuer(Options.Create(TestOptions()), new StubClock(IssuedAtUtc));
        var issued = issuer.Issue(User());

        var foreign = TestOptions();
        foreign.Key = "otra-clave-de-firma-completamente-distinta-2026";

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(issued.Token, ValidationParameters(foreign));

        Assert.False(result.IsValid);
        Assert.IsAssignableFrom<SecurityTokenException>(result.Exception);
    }

    [Fact]
    public void Issue_WithNullUser_Throws()
    {
        var issuer = new JwtSecurityTokenIssuer(Options.Create(TestOptions()), new StubClock(IssuedAtUtc));

        Assert.Throws<ArgumentNullException>(() => issuer.Issue(null!));
    }

    /// <summary>Parametros equivalentes a los que configura la API al validar el token.</summary>
    private static TokenValidationParameters ValidationParameters(SecurityOptions options) => new()
    {
        ValidIssuer = options.Issuer,
        ValidAudience = options.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(options.ResolveSigningKey()),
        ValidateIssuerSigningKey = true,
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        LifetimeValidator = (_, expires, _, _) => expires.HasValue && expires.Value > IssuedAtUtc
    };

    private sealed class StubClock : IClock
    {
        public StubClock(DateTime utcNow) => UtcNow = utcNow;

        public DateTime UtcNow { get; }

        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }
}
