using LooseNotes.Web.Data;
using LooseNotes.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LooseNotes.Web.Services;

public interface IShareTokenService
{
    // Returns the bearer token (only time it is visible). Caller is responsible
    // for sending it to the user via a confidential channel.
    Task<string> CreateAsync(int noteId, string callerUserId, bool callerIsAdmin, TimeSpan? lifetime, CancellationToken ct);
    Task<Note?> ResolveAsync(string token, CancellationToken ct);
    Task<bool> RevokeAsync(int tokenId, string callerUserId, bool callerIsAdmin, CancellationToken ct);
}

public sealed class ShareTokenService : IShareTokenService
{
    private readonly AppDbContext _db;
    private readonly INoteAuthorizationService _auth;
    private readonly ITokenHasher _hasher;
    private readonly ILogger<ShareTokenService> _log;

    public ShareTokenService(
        AppDbContext db,
        INoteAuthorizationService auth,
        ITokenHasher hasher,
        ILogger<ShareTokenService> log)
    {
        _db = db;
        _auth = auth;
        _hasher = hasher;
        _log = log;
    }

    public async Task<string> CreateAsync(int noteId, string callerUserId, bool callerIsAdmin, TimeSpan? lifetime, CancellationToken ct)
    {
        var note = await _auth.LoadOwnedAsync(noteId, callerUserId, callerIsAdmin, ct)
            ?? throw new UnauthorizedAccessException("note not found or not owned");

        var token = _hasher.NewUrlSafeToken(32);
        var record = new ShareToken
        {
            NoteId = note.Id,
            TokenHash = _hasher.Hash(token),
            ExpiresAt = lifetime is null ? null : DateTimeOffset.UtcNow + lifetime
        };
        _db.ShareTokens.Add(record);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("share_token.created note_id={NoteId} actor={Actor} token_id={TokenId}",
            note.Id, callerUserId, record.Id);
        return token;
    }

    public async Task<Note?> ResolveAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = _hasher.Hash(token);
        var record = await _db.ShareTokens
            .Include(s => s.Note)
            .FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (record is null) return null;
        if (record.RevokedAt != null) return null;
        if (record.ExpiresAt is { } exp && exp < DateTimeOffset.UtcNow) return null;
        return record.Note;
    }

    public async Task<bool> RevokeAsync(int tokenId, string callerUserId, bool callerIsAdmin, CancellationToken ct)
    {
        var record = await _db.ShareTokens.Include(s => s.Note).FirstOrDefaultAsync(s => s.Id == tokenId, ct);
        if (record is null || record.Note is null) return false;
        if (record.Note.OwnerId != callerUserId && !callerIsAdmin) return false;
        record.RevokedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("share_token.revoked id={Id} actor={Actor}", tokenId, callerUserId);
        return true;
    }
}
