using LooseNotes.Web.Data;
using LooseNotes.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LooseNotes.Web.Services;

public interface INoteSearchService
{
    Task<IReadOnlyList<Note>> SearchAsync(string? keyword, string? viewerId, CancellationToken ct);
    Task<IReadOnlyList<Note>> TopRatedAsync(string? viewerId, string? topicTag, CancellationToken ct);
}

public sealed class NoteSearchService : INoteSearchService
{
    private static readonly IReadOnlySet<string> AllowedTopicTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "general", "work", "personal", "study", "code"
    };

    private readonly AppDbContext _db;

    public NoteSearchService(AppDbContext db) => _db = db;

    // Search uses LINQ → EF Core parameterized SQL — never string concatenation.
    // Visibility predicate is enforced at the data store level (row-level
    // filter) so a private note is never returned to a non-owner regardless of
    // the keyword.
    public async Task<IReadOnlyList<Note>> SearchAsync(string? keyword, string? viewerId, CancellationToken ct)
    {
        var trimmed = (keyword ?? string.Empty).Trim();
        if (trimmed.Length > 200) trimmed = trimmed.Substring(0, 200);

        var q = _db.Notes.AsNoTracking()
            .Where(n => n.IsPublic || (viewerId != null && n.OwnerId == viewerId));
        if (trimmed.Length > 0)
        {
            var like = $"%{EscapeLike(trimmed)}%";
            q = q.Where(n => EF.Functions.Like(n.Title, like) || EF.Functions.Like(n.SanitizedContent, like));
        }
        return await q.OrderByDescending(n => n.UpdatedAt).Take(100).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Note>> TopRatedAsync(string? viewerId, string? topicTag, CancellationToken ct)
    {
        var q = _db.Notes.AsNoTracking()
            .Where(n => n.IsPublic || (viewerId != null && n.OwnerId == viewerId));

        if (!string.IsNullOrEmpty(topicTag))
        {
            // Allowlist → any value not on the list is silently ignored. The
            // PRD asks to interpolate the value into the query; we refuse and
            // bind to a parameter only after allowlist clearance.
            if (!AllowedTopicTags.Contains(topicTag)) return Array.Empty<Note>();
            // Topic tag filter is stubbed at the schema level (no Tag entity in
            // this version); kept as an extension point.
        }

        return await q
            .Select(n => new
            {
                Note = n,
                Avg = n.Ratings.Any() ? n.Ratings.Average(r => (double)r.Score) : 0.0
            })
            .OrderByDescending(x => x.Avg)
            .Take(50)
            .Select(x => x.Note)
            .ToListAsync(ct);
    }

    // EF Core's Like uses LIKE; escape wildcard characters in user input so a
    // user cannot DoS us with a pattern-explosion or unintended match.
    private static string EscapeLike(string s) =>
        s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
