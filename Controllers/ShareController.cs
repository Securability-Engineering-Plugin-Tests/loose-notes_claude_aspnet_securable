using LooseNotes.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LooseNotes.Controllers;

/// <summary>
/// Serves notes via share tokens to unauthenticated visitors.
/// SSEM: Authenticity — token validated against DB record (no sequential/guessable IDs).
/// SSEM: Confidentiality — only the specific shared note is returned, not the owner's data.
/// </summary>
public sealed class ShareController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ShareController> _logger;

    public ShareController(ApplicationDbContext db, ILogger<ShareController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet("share/{token}")]
    public async Task<IActionResult> View(string token)
    {
        // Sanitize: token must be exactly 64 hex characters (32-byte CSPRNG hex)
        if (string.IsNullOrWhiteSpace(token) ||
            token.Length != 64 ||
            !token.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
        {
            return NotFound();
        }

        var note = await _db.Notes
            .Include(n => n.Owner)
            .FirstOrDefaultAsync(n => n.ShareToken == token);

        if (note == null)
        {
            _logger.LogInformation("Share token lookup: no match");
            return NotFound();
        }

        _logger.LogInformation("Note {NoteId} accessed via share token", note.Id);
        return View(note);
    }
}
