using System.Globalization;
using System.Security.Cryptography;

namespace University_Management_System_2;

public static class PasswordHasher
{
    public const int Iterations = 600_000;
    public const int MaxPasswordLength = 256;
    private const string Prefix = "pbkdf2-sha256";
    private static readonly string DummyHash = Hash("timing-padding-value");

    public static string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (password.Length is < 1 or > MaxPasswordLength)
            throw new InvalidOperationException($"Use a password between 1 and {MaxPasswordLength} characters.");
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] digest = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        string encoded = $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(digest)}";
        CryptographicOperations.ZeroMemory(digest);
        return encoded;
    }

    public static bool Verify(string password, string? encoded)
    {
        if (password is null || password.Length is < 1 or > MaxPasswordLength || encoded is null || encoded.Length > 200)
            return false;
        string[] parts = encoded.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int rounds) || rounds != Iterations)
            return false;
        try
        {
            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expected = Convert.FromBase64String(parts[3]);
            if (salt.Length != 16 || expected.Length != 32) return false;
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, rounds, HashAlgorithmName.SHA256, 32);
            bool matches = CryptographicOperations.FixedTimeEquals(actual, expected);
            CryptographicOperations.ZeroMemory(actual);
            return matches;
        }
        catch (FormatException) { return false; }
    }

    internal static void PadMissingAccount(string password) => Verify(password, DummyHash);

    public static string NormalizeUsername(string username)
    {
        string value = username?.Trim() ?? "";
        if (value.Length is < 3 or > 100 || value.Any(char.IsControl))
            throw new InvalidOperationException("Use a username of 3–100 characters without control characters.");
        return value;
    }

    public static void ValidateNewPassword(string password)
    {
        if (password is null || password.Length is < 12 or > MaxPasswordLength || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException($"Use a password or passphrase of 12–{MaxPasswordLength} characters.");
    }
}
