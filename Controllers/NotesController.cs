using LooseNotes.Data;
using LooseNotes.Models;
using LooseNotes.Services;
using LooseNotes.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace LooseNotes.Controllers;

/// <summary>
/// CRUD for notes, ratings, search, import/export, and attachment management.
/// SSEM: Integrity — ownership checks on all mutations (IDOR prevention, ASVS V4.2).
/// SSEM: Integrity — EF Core parameterized queries (no SQL injection, ASVS V5.3).
/// SSEM: Integrity — path confinement for import/export (ASVS V5.3).
/// SSEM: Authenticity — share tokens from CSPRNG (ASVS V6.3).
/// </summary>
[Authorize]
public sealed class NotesController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<NotesController> _logger;

    public NotesController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IFileStorageService fileStorage,
        ILogger<NotesController> logger)
    {
        _db = db;
        _userManager = userManager;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    // ── List ──────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User)!;
        var notes = await _db.Notes
            .Where(n => n.OwnerId == userId)
            .OrderByDescending(n => n.UpdatedAt)
            .AsNoTracking()
            .ToListAsync();

        return View(new NoteListViewModel { Notes = notes });
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [HttpGet]
    public IActionResult Create() => View(new CreateNoteViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(CreateNoteViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var userId = _userManager.GetUserId(User)!;
        var note = new Note
        {
            Title = model.Title,
            Content = model.Content,
            IsPublic = model.IsPublic,
            OwnerId = userId  // Server-resolved; never from client (Derived Integrity Principle)
        };

        _db.Notes.Add(note);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Note {NoteId} created by user {UserId}", note.Id, userId);
        return RedirectToAction(nameof(Details), new { id = note.Id });
    }

    // ── Details ───────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var userId = _userManager.GetUserId(User)!;

        var note = await _db.Notes
            .Include(n => n.Attachments)
            .Include(n => n.Ratings).ThenInclude(r => r.Rater)
            .FirstOrDefaultAsync(n => n.Id == id &&
                (n.OwnerId == userId || n.IsPublic));

        if (note == null) return NotFound();

        return View(new NoteDetailViewModel
        {
            Note = note,
            IsOwner = note.OwnerId == userId,
            Ratings = note.Ratings.OrderByDescending(r => r.CreatedAt).ToList(),
            NewRating = new RatingSubmitViewModel { NoteId = id }
        });
    }

    // ── Edit ──────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        // IDOR prevention: verify ownership server-side (ASVS V4.2)
        var note = await _db.Notes.FirstOrDefaultAsync(
            n => n.Id == id && n.OwnerId == userId);

        if (note == null) return NotFound();

        return View(new EditNoteViewModel
        {
            Id = note.Id,
            Title = note.Title,
            Content = note.Content,
            IsPublic = note.IsPublic
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(EditNoteViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var userId = _userManager.GetUserId(User)!;
        // Reload from DB — do not bind OwnerId from client (Derived Integrity Principle)
        var note = await _db.Notes.FirstOrDefaultAsync(
            n => n.Id == model.Id && n.OwnerId == userId);

        if (note == null) return NotFound();

        note.Title = model.Title;
        note.Content = model.Content;
        note.IsPublic = model.IsPublic;
        note.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        _logger.LogInformation("Note {NoteId} updated by user {UserId}", note.Id, userId);
        return RedirectToAction(nameof(Details), new { id = note.Id });
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var note = await _db.Notes.FirstOrDefaultAsync(
            n => n.Id == id && n.OwnerId == userId);

        if (note == null) return NotFound();
        return View(note);
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        // Ownership re-verified at delete execution (ASVS V4.2)
        var note = await _db.Notes
            .Include(n => n.Attachments)
            .FirstOrDefaultAsync(n => n.Id == id && n.OwnerId == userId);

        if (note == null) return NotFound();

        foreach (var att in note.Attachments)
            await _fileStorage.DeleteAttachmentAsync(att.StoredFileName);

        _db.Notes.Remove(note);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Note {NoteId} deleted by user {UserId}", id, userId);
        return RedirectToAction(nameof(Index));
    }

    // ── Attachment Upload ─────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> UploadAttachment(int noteId, IFormFile file)
    {
        var userId = _userManager.GetUserId(User)!;
        var note = await _db.Notes.FirstOrDefaultAsync(
            n => n.Id == noteId && n.OwnerId == userId);

        if (note == null) return NotFound();
        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "No file selected";
            return RedirectToAction(nameof(Details), new { id = noteId });
        }

        try
        {
            // Extension/magic-byte validation + server-assigned name in FileStorageService
            var storedName = await _fileStorage.SaveAttachmentAsync(file);
            var attachment = new Attachment
            {
                StoredFileName = storedName,
                OriginalFileName = Path.GetFileName(file.FileName), // Display only
                ContentType = file.ContentType,
                FileSizeBytes = file.Length,
                NoteId = noteId
            };
            _db.Attachments.Add(attachment);
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Attachment uploaded for note {NoteId} by {UserId}: {StoredName}",
                noteId, userId, storedName);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(
                "Attachment upload rejected for note {NoteId}: {Reason}", noteId, ex.Message);
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = noteId });
    }

    // ── Attachment Download ───────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Download(int id)
    {
        var userId = _userManager.GetUserId(User)!;

        // Only allow download of attachments belonging to visible notes
        var attachment = await _db.Attachments
            .Include(a => a.Note)
            .FirstOrDefaultAsync(a => a.Id == id &&
                (a.Note!.OwnerId == userId || a.Note.IsPublic));

        if (attachment == null) return NotFound();

        try
        {
            // ResolveAttachmentPath enforces path confinement (ASVS V5.3)
            var path = _fileStorage.ResolveAttachmentPath(attachment.StoredFileName);
            if (!System.IO.File.Exists(path))
                return NotFound();

            var bytes = await System.IO.File.ReadAllBytesAsync(path);
            // Use original filename for download prompt — it is never used in path operations
            var safeDisplayName = Path.GetFileName(attachment.OriginalFileName);
            return File(bytes, attachment.ContentType, safeDisplayName);
        }
        catch (ArgumentException)
        {
            return NotFound();
        }
    }

    // ── Search ────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Search(string? keyword)
    {
        var model = new SearchViewModel { Keyword = keyword };

        if (string.IsNullOrWhiteSpace(keyword) || keyword.Length > 200)
            return View(model);

        var userId = _userManager.GetUserId(User)!;

        // EF Core generates parameterized queries — no string concatenation (ASVS V5.3)
        model.Results = await _db.Notes
            .Where(n => n.IsPublic || n.OwnerId == userId)
            .Where(n => EF.Functions.Like(n.Title, $"%{keyword}%") ||
                        EF.Functions.Like(n.Content, $"%{keyword}%"))
            .OrderByDescending(n => n.UpdatedAt)
            .Take(50)
            .AsNoTracking()
            .ToListAsync();

        _logger.LogInformation(
            "Search by {UserId}: keyword=<redacted> results={Count}",
            userId, model.Results.Count);

        return View(model);
    }

    // ── Rating Submission ─────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Rate(RatingSubmitViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Invalid rating submission";
            return RedirectToAction(nameof(Details), new { id = model.NoteId });
        }

        var userId = _userManager.GetUserId(User)!;

        // Verify note exists and is visible to the rater
        var note = await _db.Notes.FirstOrDefaultAsync(
            n => n.Id == model.NoteId && (n.IsPublic || n.OwnerId == userId));

        if (note == null) return NotFound();

        // Upsert: one rating per user per note (unique index enforced in DB)
        var existing = await _db.Ratings.FirstOrDefaultAsync(
            r => r.NoteId == model.NoteId && r.RaterId == userId);

        if (existing != null)
        {
            existing.Score = model.Score;
            existing.Comment = model.Comment;
        }
        else
        {
            _db.Ratings.Add(new Rating
            {
                NoteId = model.NoteId,
                Score = model.Score,
                Comment = model.Comment,
                RaterId = userId  // Server-resolved (Derived Integrity Principle)
            });
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation(
            "Rating submitted: note={NoteId} user={UserId} score={Score}",
            model.NoteId, userId, model.Score);

        return RedirectToAction(nameof(Details), new { id = model.NoteId });
    }

    // ── Top Rated ─────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> TopRated()
    {
        var notes = await _db.Notes
            .Where(n => n.IsPublic)
            .Include(n => n.Ratings)
            .OrderByDescending(n => n.Ratings.Count > 0
                ? n.Ratings.Average(r => r.Score) : 0)
            .Take(20)
            .AsNoTracking()
            .ToListAsync();

        return View(new TopRatedViewModel { Notes = notes });
    }

    // ── Email Autocomplete (authenticated only) ────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> EmailAutocomplete(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix) || prefix.Length < 2 || prefix.Length > 100)
            return Json(Array.Empty<string>());

        // EF Core parameterized query — ASVS V5.3
        var matches = await _db.Users
            .Where(u => EF.Functions.Like(u.Email!, $"{prefix}%"))
            .Select(u => u.Email)
            .Take(10)
            .ToListAsync();

        return Json(matches);
    }

    // ── Share Token Generation ────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> GenerateShareLink(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var note = await _db.Notes.FirstOrDefaultAsync(
            n => n.Id == id && n.OwnerId == userId);

        if (note == null) return NotFound();

        // CSPRNG token — never sequential/integer (ASVS V6.3)
        note.ShareToken = RandomNumberGenerator.GetHexString(32);
        await _db.SaveChangesAsync();

        var link = Url.Action("View", "Share", new { token = note.ShareToken }, Request.Scheme);
        _logger.LogInformation(
            "Share link generated for note {NoteId} by {UserId}", id, userId);

        TempData["ShareLink"] = link;
        return RedirectToAction(nameof(Details), new { id });
    }

    // ── Bulk Export ───────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Export(ExportRequestViewModel model)
    {
        if (!ModelState.IsValid || model.NoteIds.Length == 0)
            return BadRequest("No notes selected");

        var userId = _userManager.GetUserId(User)!;

        // Only export notes owned by the current user (IDOR prevention)
        var notes = await _db.Notes
            .Include(n => n.Attachments)
            .Where(n => model.NoteIds.Contains(n.Id) && n.OwnerId == userId)
            .AsNoTracking()
            .ToListAsync();

        if (notes.Count == 0) return NotFound();

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = new
            {
                exportedAt = DateTime.UtcNow,
                notes = notes.Select(n => new
                {
                    id = n.Id,
                    title = n.Title,
                    content = n.Content,
                    isPublic = n.IsPublic,
                    createdAt = n.CreatedAt,
                    attachments = n.Attachments.Select(a => new
                    {
                        filename = a.StoredFileName,
                        originalName = a.OriginalFileName,
                        contentType = a.ContentType
                    })
                })
            };

            var manifestEntry = zip.CreateEntry("notes.json");
            await using var manifestStream = manifestEntry.Open();
            await JsonSerializer.SerializeAsync(manifestStream, manifest,
                new JsonSerializerOptions { WriteIndented = true });

            foreach (var note in notes)
            {
                foreach (var att in note.Attachments)
                {
                    try
                    {
                        // Path confinement enforced in ResolveAttachmentPath
                        var srcPath = _fileStorage.ResolveAttachmentPath(att.StoredFileName);
                        if (!System.IO.File.Exists(srcPath)) continue;

                        var entry = zip.CreateEntry($"attachments/{att.StoredFileName}");
                        await using var entryStream = entry.Open();
                        await using var srcStream = System.IO.File.OpenRead(srcPath);
                        await srcStream.CopyToAsync(entryStream);
                    }
                    catch (ArgumentException ex)
                    {
                        _logger.LogWarning(
                            "Skipping attachment during export: {Reason}", ex.Message);
                    }
                }
            }
        }

        ms.Seek(0, SeekOrigin.Begin);
        var fileName = $"export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";
        return File(ms.ToArray(), "application/zip", fileName);
    }

    // ── Bulk Import ───────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Import(IFormFile archive)
    {
        if (archive == null || archive.Length == 0)
            return BadRequest("No archive provided");

        // Size limit: 50 MB for import archives
        const long MaxImportSize = 50 * 1024 * 1024;
        if (archive.Length > MaxImportSize)
            return BadRequest("Archive exceeds maximum allowed size");

        var userId = _userManager.GetUserId(User)!;

        using var ms = new MemoryStream();
        await archive.CopyToAsync(ms);
        ms.Seek(0, SeekOrigin.Begin);

        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        // Validate: enforce max entry count (ASVS V5.2.3 — zip bomb protection)
        if (zip.Entries.Count > 1000)
            return BadRequest("Archive contains too many entries");

        var manifestEntry = zip.GetEntry("notes.json");
        if (manifestEntry == null)
            return BadRequest("Invalid archive: missing notes.json");

        using var manifestStream = manifestEntry.Open();
        ExportManifest? manifest;
        try
        {
            manifest = await JsonSerializer.DeserializeAsync<ExportManifest>(manifestStream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return BadRequest("Invalid manifest format");
        }

        if (manifest?.Notes == null) return BadRequest("Empty manifest");

        foreach (var noteData in manifest.Notes)
        {
            var note = new Note
            {
                Title = noteData.Title?[..Math.Min(noteData.Title.Length, 200)] ?? "Imported Note",
                Content = noteData.Content?[..Math.Min(noteData.Content?.Length ?? 0, 50000)] ?? string.Empty,
                IsPublic = noteData.IsPublic,
                OwnerId = userId  // Always assigned to the importing user
            };
            _db.Notes.Add(note);
            await _db.SaveChangesAsync();

            if (noteData.Attachments == null) continue;

            foreach (var attData in noteData.Attachments)
            {
                // Sanitize: only use the filename component, not any directory prefix
                var safeName = Path.GetFileName(attData.Filename ?? string.Empty);
                if (string.IsNullOrWhiteSpace(safeName)) continue;

                // Only process allowed extensions (ASVS V5.2.2)
                var ext = Path.GetExtension(safeName).ToLowerInvariant();
                var allowedExts = new[] { ".pdf", ".txt", ".md", ".png", ".jpg", ".jpeg",
                                          ".gif", ".csv", ".docx", ".xlsx" };
                if (!allowedExts.Contains(ext)) continue;

                var archivePath = $"attachments/{safeName}";
                var attEntry = zip.GetEntry(archivePath);
                if (attEntry == null) continue;

                if (attEntry.Length > 10 * 1024 * 1024) continue; // skip oversized

                // Server-assigned stored name (Derived Integrity Principle)
                var storedName = $"{Guid.NewGuid()}{ext}";
                try
                {
                    var destPath = _fileStorage.ResolveAttachmentPath(storedName);
                    using var entryStream = attEntry.Open();
                    await using var dest = new FileStream(
                        destPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await entryStream.CopyToAsync(dest);

                    _db.Attachments.Add(new Attachment
                    {
                        StoredFileName = storedName,
                        OriginalFileName = safeName,
                        ContentType = attData.ContentType ?? "application/octet-stream",
                        FileSizeBytes = attEntry.Length,
                        NoteId = note.Id
                    });
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning(
                        "Import: skipping attachment due to path violation: {Reason}", ex.Message);
                }
            }

            await _db.SaveChangesAsync();
        }

        _logger.LogInformation("Import completed by user {UserId}: {Count} notes",
            userId, manifest.Notes.Count);

        return RedirectToAction(nameof(Index));
    }

    // ── XML Processing ────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> ProcessXml(IFormFile xmlFile)
    {
        if (xmlFile == null || xmlFile.Length == 0)
            return BadRequest("No XML file provided");

        if (xmlFile.Length > 1 * 1024 * 1024) // 1 MB limit for XML
            return BadRequest("XML file too large");

        using var stream = xmlFile.OpenReadStream();
        var settings = new XmlReaderSettings
        {
            // XXE prevention: disable DTD processing entirely (ASVS V5.5 / CWE-611)
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = 100_000
        };

        try
        {
            using var reader = XmlReader.Create(stream, settings);
            while (await reader.ReadAsync()) { /* validate structure only */ }
        }
        catch (XmlException ex)
        {
            _logger.LogWarning("XML processing error: {Message}", ex.Message);
            return BadRequest("Invalid XML document");
        }

        return Ok("XML processed successfully");
    }

    // ── Diagnostics (authenticated only) ──────────────────────────────────────

    [HttpGet]
    public IActionResult Diagnostics()
    {
        // Collect headers safely — values are HTML-encoded by Razor when rendered
        var headers = Request.Headers
            .Select(h => new { h.Key, Value = h.Value.ToString() })
            .ToList();

        return View(headers);
    }
}

// ── Import/Export manifest DTOs ───────────────────────────────────────────────

internal sealed class ExportManifest
{
    public DateTime ExportedAt { get; set; }
    public List<NoteExportDto>? Notes { get; set; }
}

internal sealed class NoteExportDto
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? Content { get; set; }
    public bool IsPublic { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<AttachmentExportDto>? Attachments { get; set; }
}

internal sealed class AttachmentExportDto
{
    public string? Filename { get; set; }
    public string? OriginalName { get; set; }
    public string? ContentType { get; set; }
}
