using System.Security.Cryptography;
using System.Text;

namespace LooseNotes.Web.Services;

public interface IEncryptionService
{
    string EncryptToBase64(string plaintext);
    string DecryptFromBase64(string ciphertext);
}

// AES-GCM authenticated encryption with a key supplied via configuration
// (loaded from environment / secrets store). No hardcoded fallback passphrase
// (PRD §24 mandated one — explicitly rejected here). A fresh 96-bit nonce is
// generated per operation; the resulting blob carries [nonce | tag |
// ciphertext] so callers cannot accidentally reuse a nonce.
//
// FIASSE: Confidentiality (S3.2.2.1), Integrity (S3.2.3.2 — tag verifies),
// Resilience (specific exception handling), Observability (caller logs only
// the failure category, never the ciphertext).
public sealed class EncryptionService : IEncryptionService, IDisposable
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly AesGcm _aes;

    public EncryptionService(IConfiguration config)
    {
        var b64 = config["Encryption:KeyBase64"]
            ?? throw new InvalidOperationException(
                "Encryption:KeyBase64 is not configured. Generate a 32-byte key and supply it via environment / user-secrets.");
        var key = Convert.FromBase64String(b64);
        if (key.Length != 32)
            throw new InvalidOperationException("Encryption key must be 32 bytes (AES-256).");
        _aes = new AesGcm(key, TagSize);
    }

    public string EncryptToBase64(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];
        _aes.Encrypt(nonce, plainBytes, cipher, tag);

        var blob = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, blob, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, blob, NonceSize + TagSize, cipher.Length);
        return Convert.ToBase64String(blob);
    }

    public string DecryptFromBase64(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrEmpty(ciphertext);
        var blob = Convert.FromBase64String(ciphertext);
        if (blob.Length < NonceSize + TagSize)
            throw new CryptographicException("ciphertext is too short");

        var nonce = blob.AsSpan(0, NonceSize);
        var tag = blob.AsSpan(NonceSize, TagSize);
        var cipher = blob.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        _aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    public void Dispose() => _aes.Dispose();
}
