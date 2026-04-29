using LooseNotes.Web.Data;
using LooseNotes.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LooseNotes.Web.Services;

public interface INoteAuthorizationService
{
    // Loads the note and asserts the caller owns it (or is an admin). Returns
    // null if it does not exist or the caller has no claim — the caller must
    // not differentiate the two reasons in HTTP responses (avoids enumeration).
    Task<Note?> LoadOwnedAsync(int noteId, string callerUserId, bool callerIsAdmin, CancellationToken ct);
}

// Centralized ownership enforcement. Every state-changing operation on a note
// MUST go through this service so the ownership check exists in exactly one
// place — controllers cannot accidentally skip it (FIASSE: Modifiability,
// Derived Integrity S4.4.1.2).
public sealed class NoteAuthorizationService : INoteAuthorizationService
{
    private readonly AppDbContext _db;
    private readonly ILogger<NoteAuthorizationService> _log;

    public NoteAuthorizationService(AppDbContext db, ILogger<NoteAuthorizationService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<Note?> LoadOwnedAsync(int noteId, string callerUserId, bool callerIsAdmin, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(callerUserId)) return null;

        var note = await _db.Notes
            .Include(n => n.Attachments)
            .FirstOrDefaultAsync(n => n.Id == noteId, ct);
        if (note is null)
        {
            _log.LogInformation("note.access.denied note_id={NoteId} actor={Actor} reason=not_found",
                noteId, callerUserId);
            return null;
        }

        if (note.OwnerId != callerUserId && !callerIsAdmin)
        {
            _log.LogWarning("note.access.denied note_id={NoteId} actor={Actor} reason=not_owner",
                noteId, callerUserId);
            return null;
        }

        _log.LogInformation("note.access.granted note_id={NoteId} actor={Actor}", noteId, callerUserId);
        return note;
    }
}
