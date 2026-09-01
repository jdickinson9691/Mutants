using System.Security.Cryptography;

namespace ChronoTravelers.Server;

/// <summary>PBKDF2-SHA256 password hashing for accounts — 16-byte salt, 32-byte hash, 100k iterations.</summary>
internal static class PasswordHash
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int Iterations = 100_000;

    public static (string Salt, string Hash) Create(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return (Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public static bool Verify(string password, string saltB64, string hashB64)
    {
        try
        {
            var salt = Convert.FromBase64String(saltB64);
            var expected = Convert.FromBase64String(hashB64);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }
}
