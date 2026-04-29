using System.Security.Claims;
using LooseNotes.Web.Data;
using LooseNotes.Web.Data.Entities;
using LooseNotes.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LooseNotes.Web.Controllers;

[Authorize]
public class RatingsController : Controller
{
    private readonly AppDbContext _db;
    private readonly ILogger<RatingsController> _log;

    public RatingsController(AppDbContext db, ILogger<RatingsController> log)
    {
        _db = db; _log = log;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(RatingInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return RedirectToAction("Details", "Notes", new { id = input.NoteId });

        var note = await _db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == input.NoteId, ct);
        if (note is null) return NotFound();
        // Users may rate a note only if it's public or owned by them; rating
        // your own note is allowed by the schema but typically suppressed in UI.
        if (!note.IsPublic && note.OwnerId != CurrentUserId) return NotFound();

        var existing = await _db.Ratings
            .FirstOrDefaultAsync(r => r.NoteId == note.Id && r.SubmitterId == CurrentUserId, ct);
        if (existing is null)
        {
            // Rating + comment are bound through EF Core (parameterized SQL) —
            // never string-concatenated. Comment is plaintext; rendering encodes.
            _db.Ratings.Add(new Rating
            {
                NoteId = note.Id,
                SubmitterId = CurrentUserId,
                Score = input.Score,
                Comment = input.Comment
            });
        }
        else
        {
            existing.Score = input.Score;
            existing.Comment = input.Comment;
        }

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("rating.submitted note_id={NoteId} actor={Actor} score={Score}",
            note.Id, CurrentUserId, input.Score);
        return RedirectToAction("Details", "Notes", new { id = note.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        var ratings = await _db.Ratings.AsNoTracking()
            .Include(r => r.Note)
            .Include(r => r.Submitter)
            .Where(r => r.Note!.OwnerId == CurrentUserId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
        return View(ratings);
    }
}
