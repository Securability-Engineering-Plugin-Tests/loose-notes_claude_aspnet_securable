using System.Security.Claims;
using LooseNotes.Web.Data;
using LooseNotes.Web.Data.Entities;
using LooseNotes.Web.Models;
using LooseNotes.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LooseNotes.Web.Controllers;

[Authorize]
public class NotesController : Controller
{
    private readonly AppDbContext _db;
    private readonly INoteAuthorizationService _auth;
    private readonly IHtmlSanitizationService _sanitizer;
    private readonly IAttachmentStorageService _attachments;
    private readonly ILogger<NotesController> _log;

    public NotesController(
        AppDbContext db,
        INoteAuthorizationService auth,
        IHtmlSanitizationService sanitizer,
        IAttachmentStorageService attachments,
        ILogger<NotesController> log)
    {
        _db = db; _auth = auth; _sanitizer = sanitizer;
        _attachments = attachments; _log = log;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private bool CurrentUserIsAdmin => User.IsInRole(Seeder.AdminRole);

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var notes = await _db.Notes.AsNoTracking()
            .Where(n => n.OwnerId == CurrentUserId)
            .OrderByDescending(n => n.UpdatedAt)
            .ToListAsync(ct);
        return View(notes);
    }

    [HttpGet]
    public IActionResult Create() => View(new NoteCreateInput());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(NoteCreateInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(input);

        var note = new Note
        {
            Title = input.Title.Trim(),
            SanitizedContent = _sanitizer.SanitizeRichText(input.Content),
            IsPublic = input.IsPublic, // explicit; defaults to false in the form
            OwnerId = CurrentUserId
        };
        _db.Notes.Add(note);
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("note.created id={Id} actor={Actor} is_public={Public}",
            note.Id, CurrentUserId, note.IsPublic);
        return RedirectToAction(nameof(Details), new { id = note.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var note = await _db.Notes.AsNoTracking()
            .Include(n => n.Attachments)
            .Include(n => n.Ratings)
            .ThenInclude(r => r.Submitter)
            .FirstOrDefaultAsync(n => n.Id == id, ct);
        if (note is null) return NotFound();

        // A note can be viewed by: its owner, an admin, or anyone if public.
        if (!note.IsPublic && note.OwnerId != CurrentUserId && !CurrentUserIsAdmin)
            return NotFound(); // 404 (not 403) — do not signal existence

        return View(note);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var note = await _auth.LoadOwnedAsync(id, CurrentUserId, CurrentUserIsAdmin, ct);
        if (note is null) return NotFound();
        return View(new NoteEditInput
        {
            Id = note.Id,
            Title = note.Title,
            Content = note.SanitizedContent,
            IsPublic = note.IsPublic
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(NoteEditInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(input);
        var note = await _auth.LoadOwnedAsync(input.Id, CurrentUserId, CurrentUserIsAdmin, ct);
        if (note is null) return NotFound();

        note.Title = input.Title.Trim();
        note.SanitizedContent = _sanitizer.SanitizeRichText(input.Content);
        note.IsPublic = input.IsPublic;
        note.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("note.updated id={Id} actor={Actor}", note.Id, CurrentUserId);
        return RedirectToAction(nameof(Details), new { id = note.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var note = await _auth.LoadOwnedAsync(id, CurrentUserId, CurrentUserIsAdmin, ct);
        if (note is null) return NotFound();
        _db.Notes.Remove(note);
        await _db.SaveChangesAsync(ct);
        _log.LogWarning("note.deleted id={Id} actor={Actor}", id, CurrentUserId);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Upload(int id, IFormFile file, CancellationToken ct)
    {
        var note = await _auth.LoadOwnedAsync(id, CurrentUserId, CurrentUserIsAdmin, ct);
        if (note is null) return NotFound();
        try
        {
            await _attachments.SaveAsync(note.Id, CurrentUserId, file, ct);
        }
        catch (InvalidAttachmentException ex)
        {
            TempData["AttachmentError"] = ex.Message;
        }
        catch (PathTraversalException ex)
        {
            _log.LogWarning("attachment.upload_rejected reason={Reason}", ex.Message);
            TempData["AttachmentError"] = "That filename was rejected.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }
}
