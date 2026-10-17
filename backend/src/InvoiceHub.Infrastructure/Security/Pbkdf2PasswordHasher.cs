using System.Security.Cryptography;
using InvoiceHub.Application.Ports;

namespace InvoiceHub.Infrastructure.Security;

/// <summary>
/// Derivacion y verificacion de contrasenas con PBKDF2-HMAC-SHA256. El formato almacenado es
/// <c>pbkdf2-sha256$iteraciones$sal$hash</c> (sal y hash en Base64), de modo que el parametro de costo
/// pueda endurecerse mas adelante sin invalidar las contrasenas existentes.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Algorithm = "pbkdf2-sha256";
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

        return string.Join('$', Algorithm, Iterations.ToString(), Convert.ToBase64String(salt), Convert.ToBase64String(key));
    }

    public bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(passwordHash))
        {
            return false;
        }

        var parts = passwordHash.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], Algorithm, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        // Comparacion en tiempo constante: no se filtra informacion por el tiempo de respuesta.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
