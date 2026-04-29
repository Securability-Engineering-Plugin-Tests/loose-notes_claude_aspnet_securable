using LooseNotes.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;

namespace LooseNotes.Web.Services;

// Wraps Identity's PasswordHasher so security answers can be hashed and
// verified using the same algorithm/parameters as passwords. Centralized here
// so the answer normalization (trim + lowercase) is consistent on write and
// verify paths.
public interface ISecurityAnswerHasher
{
    string Hash(string answer);
    bool Verify(string suppliedAnswer, string storedHash);
}

public sealed class SecurityAnswerHasher : ISecurityAnswerHasher
{
    private static readonly ApplicationUser _shim = new();
    private readonly IPasswordHasher<ApplicationUser> _hasher;

    public SecurityAnswerHasher(IPasswordHasher<ApplicationUser> hasher) => _hasher = hasher;

    public string Hash(string answer)
    {
        var normalized = (answer ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) throw new ArgumentException("answer is empty", nameof(answer));
        return _hasher.HashPassword(_shim, normalized);
    }

    public bool Verify(string suppliedAnswer, string storedHash)
    {
        if (string.IsNullOrEmpty(storedHash)) return false;
        var normalized = (suppliedAnswer ?? string.Empty).Trim().ToLowerInvariant();
        return _hasher.VerifyHashedPassword(_shim, storedHash, normalized) != PasswordVerificationResult.Failed;
    }
}
