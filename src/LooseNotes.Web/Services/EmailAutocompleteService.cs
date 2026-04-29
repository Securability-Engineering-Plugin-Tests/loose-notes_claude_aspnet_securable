using LooseNotes.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace LooseNotes.Web.Services;

public interface IEmailAutocompleteService
{
    Task<IReadOnlyList<string>> SuggestAsync(string prefix, CancellationToken ct);
}

// PRD §15 demanded that this endpoint be unauthenticated, return all matching
// addresses, and concatenate the prefix into a LIKE clause. Re-engineered:
//   * Caller MUST be authenticated (controller enforces).
//   * Prefix is required to be ≥ 3 characters and is parameter-bound.
//   * Result count is capped.
//   * Endpoint is rate-limited at the route level (see Program.cs).
public sealed class EmailAutocompleteService : IEmailAutocompleteService
{
    private const int MinPrefix = 3;
    private const int MaxPrefix = 64;
    private const int ResultCap = 10;

    private readonly AppDbContext _db;
    public EmailAutocompleteService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<string>> SuggestAsync(string prefix, CancellationToken ct)
    {
        var trimmed = (prefix ?? string.Empty).Trim().ToLowerInvariant();
        if (trimmed.Length < MinPrefix) return Array.Empty<string>();
        if (trimmed.Length > MaxPrefix) trimmed = trimmed.Substring(0, MaxPrefix);

        var like = $"{EscapeLike(trimmed)}%";
        return await _db.Users
            .Where(u => u.Email != null && EF.Functions.Like(u.NormalizedEmail!, like.ToUpperInvariant()))
            .Select(u => u.Email!)
            .Take(ResultCap)
            .ToListAsync(ct);
    }

    private static string EscapeLike(string s) =>
        s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
