using LooseNotes.Data;
using LooseNotes.Models;
using LooseNotes.Services;
using LooseNotes.ViewModels.Notes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LooseNotes.Controllers;

[Authorize]
public class NotesController : Controller
{
    // Allowed file extensions and max size (10 MB)
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".doc", ".docx", ".txt", ".png", ".jpg", ".jpeg" };
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IFileStorageService _fileStorage;
    private readonly IShareTokenService _tokenService;
    private readonly IAuditService _audit;

    public NotesController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IFileStorageService fileStorage,
        IShareTokenService tokenService,
        IAuditService audit)
    {
        _db = db;
        _userManager = userManager;
        _fileStorage = fileStorage;
        _tokenService = tokenService;
        _audit = audit;
    }

    // ── List ─────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User)!;

        var notes = await _db.Notes
            .Where(n => n.OwnerId == userId)
            .Include(n => n.Ratings)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new NoteListItemViewModel
            {
                Id = n.Id,
                Title = n.Title,
                Excerpt = n.Content.Length > 200 ? n.Content[..200] : n.Content,
                IsPublic = n.IsPublic,
                OwnerUserName = n.Owner!.UserName!,
                CreatedAt = n.CreatedAt,
                AverageRating = n.Ratings.Any() ? n.Ratings.Average(r => r.Value) : 0,
                RatingCount = n.Ratings.Count
            })
            .ToListAsync();

        return View(notes);
    }

    // ── Details ───────────────────────────────────────────────────────────────

    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> Details(int id)
    {
        var userId = _userManager.GetUserId(User);

        var note = await _db.Notes
            .Include(n => n.Owner)
            .Include(n => n.Attachments)
            .Include(n => n.Ratings).ThenInclude(r => r.Rater)
            .Include(n => n.ShareLinks)
            .FirstOrDefaultAsync(n => n.Id == id);

        if (note is null) return NotFound();

        // Access control: private notes visible to owner and admins only
        bool isOwner = userId is not null && note.OwnerId == userId;
        bool isAdmin = User.IsInRole("Admin");

        if (!note.IsPublic && !isOwner && !isAdmin)
            return Forbid();

        var currentUserRating = userId is null
            ? null
            : note.Ratings.FirstOrDefault(r => r.RaterId == userId);

        var vm = new NoteDetailsViewModel
        {
            Id = note.Id,
            Title = note.Title,
            Content = note.Content,
            IsPublic = note.IsPublic,
            CreatedAt = note.CreatedAt,
            UpdatedAt = note.UpdatedAt,
            OwnerUserName = note.Owner?.UserName ?? "Unknown",
            IsOwner = isOwner,
            IsAdmin = isAdmin,
            Attachments = note.Attachments.ToList(),
            Ratings = note.Ratings.OrderByDescending(r => r.CreatedAt).ToList(),
            AverageRating = note.Ratings.Any() ? note.Ratings.Average(r => r.Value) : 0,
            CurrentUserRatingId = currentUserRating?.Id,
            CurrentUserRatingValue = currentUserRating?.Value,
            ActiveShareLinks = note.ShareLinks.Where(s => !s.IsRevoked).ToList()
        };

        return View(vm);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [HttpGet]
    public IActionResult Create() => View(new CreateNoteViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateNoteViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        // Derived Integrity: OwnerId from session, never from request body
        var userId = _userManager.GetUserId(User)!;

        var note = new Note
        {
            Title = model.Title.Trim(),
            Content = model.Content,
            IsPublic = model.IsPublic,
            OwnerId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Notes.Add(note);
        await _db.SaveChangesAsync();

        if (model.Attachment is not null)
        {
            var attachError = await ValidateAndStoreAttachmentAsync(model.Attachment, note, userId);
            if (attachError is not null)
            {
                ModelState.AddModelError(string.Empty, attachError);
                // Note created — continue without attachment
            }
            else
            {
                await _db.SaveChangesAsync();
            }
        }

        await _audit.LogAsync("NoteCreated", userId, true,
            targetId: note.Id.ToString(), targetType: "Note");

        return RedirectToAction(nameof(Details), new { id = note.Id });
    }

    // ── Edit ──────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var note = await _db.Notes.FindAsync(id);

        if (note is null) return NotFound();
        if (!IsOwnerOrAdmin(note.OwnerId, userId)) return Forbid();

        return View(new EditNoteViewModel
        {
            Id = note.Id,
            Title = note.Title,
            Content = note.Content,
            IsPublic = note.IsPublic
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditNoteViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var userId = _userManager.GetUserId(User)!;
        var note = await _db.Notes.FindAsync(model.Id);

        if (note is null) return NotFound();
        if (!IsOwnerOrAdmin(note.OwnerId, userId)) return Forbid();

        // Request surface minimization: only update allowed fields
        note.Title = model.Title.Trim();
        note.Content = model.Content;
        note.IsPublic = model.IsPublic;
        note.UpdatedAt = DateTime.UtcNow;

        if (model.NewAttachment is not null)
        {
            var attachError = await ValidateAndStoreAttachmentAsync(model.NewAttachment, note, userId);
            if (attachError is not null)
                ModelState.AddModelError(string.Empty, attachError);
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync("NoteEdited", userId, true,
            targetId: note.Id.ToString(), targetType: "Note");

        return RedirectToAction(nameof(Details), new { id = note.Id });
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var note = await _db.Notes.Include(n => n.Owner).FirstOrDefaultAsync(n => n.Id == id);

        if (note is null) return NotFound();
        if (!IsOwnerOrAdmin(note.OwnerId, userId)) return Forbid();

        return View(note);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var note = await _db.Notes
            .Include(n => n.Attachments)
            .FirstOrDefaultAsync(n => n.Id == id);

        if (note is null) return NotFound();
        if (!IsOwnerOrAdmin(note.OwnerId, userId)) return Forbid();

        foreach (var attachment in note.Attachments)
            await _fileStorage.DeleteAsync(attachment.StoredFileName);

        _db.Notes.Remove(note);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("NoteDeleted", userId, true,
            targetId: id.ToString(), targetType: "Note");

        return RedirectToAction(nameof(Index));
    }

    // ── Search ────────────────────────────────────────────────────────────────

    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> Search(string? q)
    {
        var userId = _userManager.GetUserId(User);
        var model = new NoteSearchViewModel { Query = q };

        if (string.IsNullOrWhiteSpace(q))
            return View(model);

        var term = q.Trim().ToLower();

        var query = _db.Notes
            .Include(n => n.Owner)
            .Include(n => n.Ratings)
            .Where(n =>
                (n.IsPublic || n.OwnerId == userId) &&
                (n.Title.ToLower().Contains(term) || n.Content.ToLower().Contains(term)));

        model.Results = await query
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new NoteListItemViewModel
            {
                Id = n.Id,
                Title = n.Title,
                Excerpt = n.Content.Length > 200 ? n.Content[..200] : n.Content,
                IsPublic = n.IsPublic,
                OwnerUserName = n.Owner!.UserName!,
                CreatedAt = n.CreatedAt,
                AverageRating = n.Ratings.Any() ? n.Ratings.Average(r => r.Value) : 0,
                RatingCount = n.Ratings.Count
            })
            .ToListAsync();

        return View(model);
    }

    // ── Top Rated ─────────────────────────────────────────────────────────────

    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> TopRated()
    {
        var notes = await _db.Notes
            .Include(n => n.Owner)
            .Include(n => n.Ratings)
            .Where(n => n.IsPublic && n.Ratings.Count >= 3)
            .OrderByDescending(n => n.Ratings.Average(r => r.Value))
            .Select(n => new NoteListItemViewModel
            {
                Id = n.Id,
                Title = n.Title,
                Excerpt = n.Content.Length > 200 ? n.Content[..200] : n.Content,
                IsPublic = true,
                OwnerUserName = n.Owner!.UserName!,
                CreatedAt = n.CreatedAt,
                AverageRating = n.Ratings.Average(r => r.Value),
                RatingCount = n.Ratings.Count
            })
            .ToListAsync();

        return View(new TopRatedViewModel { Notes = notes });
    }

    // ── Share Links ───────────────────────────────────────────────────────────

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateShareLink(int noteId)
    {
        var userId = _userManager.GetUserId(User)!;
        var note = await _db.Notes.FindAsync(noteId);

        if (note is null) return NotFound();
        if (note.OwnerId != userId) return Forbid();

        var token = _tokenService.GenerateToken();
        _db.ShareLinks.Add(new ShareLink
        {
            NoteId = noteId,
            Token = token,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        await _audit.LogAsync("ShareLinkCreated", userId, true,
            targetId: noteId.ToString(), targetType: "Note");

        return RedirectToAction(nameof(Details), new { id = noteId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeShareLink(int shareLinkId, int noteId)
    {
        var userId = _userManager.GetUserId(User)!;
        var link = await _db.ShareLinks.Include(s => s.Note).FirstOrDefaultAsync(s => s.Id == shareLinkId);

        if (link is null) return NotFound();
        if (link.Note?.OwnerId != userId) return Forbid();

        link.IsRevoked = true;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("ShareLinkRevoked", userId, true,
            targetId: shareLinkId.ToString(), targetType: "ShareLink");

        return RedirectToAction(nameof(Details), new { id = noteId });
    }

    // ── Ratings ───────────────────────────────────────────────────────────────

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Rate(RatingInputViewModel model)
    {
        if (!ModelState.IsValid)
            return RedirectToAction(nameof(Details), new { id = model.NoteId });

        var userId = _userManager.GetUserId(User)!;

        // Derived Integrity: RaterId from session
        var existing = await _db.Ratings
            .FirstOrDefaultAsync(r => r.NoteId == model.NoteId && r.RaterId == userId);

        if (existing is not null)
        {
            existing.Value = model.Value;
            existing.Comment = model.Comment;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.Ratings.Add(new Rating
            {
                NoteId = model.NoteId,
                RaterId = userId,
                Value = model.Value,
                Comment = model.Comment,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id = model.NoteId });
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private bool IsOwnerOrAdmin(string ownerId, string currentUserId) =>
        ownerId == currentUserId || User.IsInRole("Admin");

    private async Task<string?> ValidateAndStoreAttachmentAsync(
        IFormFile file, Note note, string userId)
    {
        // Canonicalize → Sanitize → Validate
        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();

        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            return $"File type '{ext}' is not allowed.";

        if (file.Length > MaxFileSizeBytes)
            return "File exceeds the 10 MB size limit.";

        if (file.Length == 0)
            return "File is empty.";

        // OriginalFileName stored for display only; StoredFileName is UUID-based
        var storedName = await _fileStorage.StoreAsync(file);
        var safeOriginalName = Path.GetFileName(file.FileName);

        note.Attachments.Add(new Attachment
        {
            StoredFileName = storedName,
            OriginalFileName = safeOriginalName,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            UploadedAt = DateTime.UtcNow,
            UploadedById = userId
        });

        return null;
    }
}
