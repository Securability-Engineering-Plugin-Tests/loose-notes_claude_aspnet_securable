using LooseNotes.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LooseNotes.Controllers;

/// <summary>
/// Handles anonymous access to notes via share tokens.
/// Authenticity: token must exist, be un-revoked, and match a real note.
/// </summary>
public class ShareController : Controller
{
    private readonly ApplicationDbContext _db;

    public ShareController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet("share/{token}")]
    public async Task<IActionResult> View(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest("Invalid share token.");

        var link = await _db.ShareLinks
            .Include(s => s.Note)
                .ThenInclude(n => n!.Owner)
            .Include(s => s.Note)
                .ThenInclude(n => n!.Attachments)
            .Include(s => s.Note)
                .ThenInclude(n => n!.Ratings)
            .FirstOrDefaultAsync(s => s.Token == token && !s.IsRevoked);

        if (link is null)
            return NotFound("This share link is invalid or has been revoked.");

        return View(link.Note);
    }
}
