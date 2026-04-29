using System.Security.Cryptography;
using System.Text;

namespace LooseNotes.Web.Services;

public interface ITokenHasher
{
    string Hash(string token);
    bool Verify(string token, string storedHash);
    string NewUrlSafeToken(int byteLength = 32);
}

// Share-link tokens (PRD §10) and reset-ticket ids must be unguessable AND
// uncomparable when leaked from the database. This module produces tokens via
// CSPRNG (RandomNumberGenerator) and stores SHA-256 of the token at rest;
// verification uses fixed-time comparison.
//
// FIASSE: Authenticity (S3.2.2.3), Confidentiality (S3.2.2.1).
public sealed class TokenHasher : ITokenHasher
{
    public string NewUrlSafeToken(int byteLength = 32)
    {
        if (byteLength < 16) throw new ArgumentOutOfRangeException(nameof(byteLength));
        var raw = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(raw)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    public bool Verify(string token, string storedHash)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(storedHash)) return false;
        var computed = Hash(token);
        var a = Encoding.ASCII.GetBytes(computed);
        var b = Encoding.ASCII.GetBytes(storedHash);
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
