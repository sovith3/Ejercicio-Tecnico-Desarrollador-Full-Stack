using System.Globalization;
using System.Text;

namespace InvoiceHub.Infrastructure.Configuration;

/// <summary>Configuracion de seguridad: firma de los tokens JWT y usuario administrador inicial.</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public string Issuer { get; set; } = "InvoiceHub";

    public string Audience { get; set; } = "InvoiceHub.Client";

    /// <summary>Clave simetrica de firma (HMAC-SHA256). Debe tener al menos 32 caracteres.</summary>
    public string Key { get; set; } = "invoicehub-development-signing-key-change-me";

    public int TokenLifetimeMinutes { get; set; } = 480;

    /// <summary>
    /// Cuando es <c>false</c> (valor por omision) los endpoints de lectura son publicos y las
    /// operaciones de escritura siguen exigiendo token: la autenticacion es opcional segun las bases.
    /// </summary>
    public bool RequireAuthentication { get; set; }

    public string AdminUsername { get; set; } = "admin";

    public string AdminPassword { get; set; } = "InvoiceHub.2026";

    public string AdminDisplayName { get; set; } = "Administrador InvoiceHub";

    public string AdminRole { get; set; } = "Administrator";

    public TimeSpan TokenLifetime => TimeSpan.FromMinutes(Math.Clamp(TokenLifetimeMinutes, 5, 1440));

    /// <summary>Clave de firma con longitud suficiente para HMAC-SHA256.</summary>
    public byte[] ResolveSigningKey()
    {
        var material = string.IsNullOrWhiteSpace(Key)
            ? string.Create(CultureInfo.InvariantCulture, $"invoicehub-{Guid.Empty:D}")
            : Key;

        if (material.Length < 32)
        {
            material = material.PadRight(32, 'x');
        }

        return Encoding.UTF8.GetBytes(material);
    }
}
