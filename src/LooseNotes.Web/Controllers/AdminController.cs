using System.Security.Claims;
using LooseNotes.Web.Data;
using LooseNotes.Web.Models;
using LooseNotes.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LooseNotes.Web.Controllers;

// Admin features (PRD §18, §19, §22).
//
// PRD §18 mandated:
//   * Verb-specific authorization that lists only GET/POST and lets PUT/PATCH/
//     DELETE through unchecked.
//   * A page that executes shell commands from a request parameter.
//   * An endpoint that re-initializes the data store with caller-supplied
//     connection parameters and no role check.
//   * Logging of unsanitized request payloads.
//
// FIASSE-aligned alternative:
//   * [Authorize(Policy = "AdminOnly")] applies to every action regardless of
//     verb — there is no path through this controller that bypasses the role.
//   * No shell-execution endpoint exists. The dashboard surfaces operational
//     metrics, user moderation, and reassignment instead.
//   * Data-store reinitialization is removed entirely — operators perform
//     migrations via the EF Core CLI, not via the web UI.
//   * Logging records IDs and outcomes only. We never log raw request bodies.
[Authorize(Policy = "AdminOnly")]
[Route("Admin")]
public class AdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly INoteAuthorizationService _auth;
    private readonly ILogger<AdminController> _log;

    public AdminController(AppDbContext db, INoteAuthorizationService auth, ILogger<AdminController> log)
    {
        _db = db; _auth = auth; _log = log;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var snapshot = new
        {
            UserCount = await _db.Users.CountAsync(ct),
            NoteCount = await _db.Notes.CountAsync(ct),
            AttachmentCount = await _db.Attachments.CountAsync(ct),
            ShareTokenCount = await _db.ShareTokens.CountAsync(ct),
        };
        return View(snapshot);
    }

    // Note-ownership reassignment (PRD §19). The PRD allowed an admin to
    // reassign without verifying ownership — we keep that capability (admins
    // are by definition empowered) but require it to flow through this single
    // POST that:
    //   * Validates target user exists.
    //   * Logs old/new owner IDs and the admin's identity.
    //   * Refuses to reassign to a non-existent user.
    [HttpPost("Reassign"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Reassign(ReassignInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest();

        var note = await _db.Notes.FirstOrDefaultAsync(n => n.Id == input.NoteId, ct);
        if (note is null) return NotFound();

        var target = await _db.Users.FindAsync(new object?[] { input.TargetUserId }, ct);
        if (target is null) return BadRequest("target user does not exist");

        var previous = note.OwnerId;
        note.OwnerId = target.Id;
        note.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        _log.LogWarning("admin.note_reassigned note_id={NoteId} previous_owner={Prev} new_owner={Next} admin={Admin}",
            note.Id, previous, target.Id, CurrentUserId);
        return RedirectToAction(nameof(Index));
    }
}
