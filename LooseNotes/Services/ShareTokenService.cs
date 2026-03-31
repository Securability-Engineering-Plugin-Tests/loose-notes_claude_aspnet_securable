using System.Security.Cryptography;

namespace LooseNotes.Services;

/// <summary>
/// Authenticity: tokens are generated using a CSPRNG — not predictable or guessable.
/// </summary>
public class ShareTokenService : IShareTokenService
{
    public string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
