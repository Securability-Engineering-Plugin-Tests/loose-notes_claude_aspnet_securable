using LooseNotes.Data;
using LooseNotes.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LooseNotes.Models;

namespace LooseNotes.Controllers;

[Authorize]
public class AttachmentsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IFileStorageService _fileStorage;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditService _audit;

    public AttachmentsController(
        ApplicationDbContext db,
        IFileStorageService fileStorage,
        UserManager<ApplicationUser> userManager,
        IAuditService audit)
    {
        _db = db;
        _fileStorage = fileStorage;
        _userManager = userManager;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> Download(int id)
    {
        var userId = _userManager.GetUserId(User)!;

        var attachment = await _db.Attachments
            .Include(a => a.Note)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (attachment is null) return NotFound();

        // Access control: only owner, admin, or public-note viewers can download
        var note = attachment.Note!;
        bool isOwner = note.OwnerId == userId;
        bool isAdmin = User.IsInRole("Admin");

        if (!note.IsPublic && !isOwner && !isAdmin)
            return Forbid();

        // GetAbsolutePath validates the stored filename format — prevents path traversal
        string filePath;
        try
        {
            filePath = _fileStorage.GetAbsolutePath(attachment.StoredFileName);
        }
        catch (ArgumentException)
        {
            await _audit.LogAsync("AttachmentDownload", userId, false,
                targetId: id.ToString(), targetType: "Attachment",
                details: "Invalid stored filename rejected");
            return BadRequest("Invalid file reference.");
        }

        if (!System.IO.File.Exists(filePath))
            return NotFound();

        var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
        var safeDownloadName = Path.GetFileName(attachment.OriginalFileName);

        await _audit.LogAsync("AttachmentDownload", userId, true,
            targetId: id.ToString(), targetType: "Attachment");

        return File(fileBytes, attachment.ContentType, safeDownloadName);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, int noteId)
    {
        var userId = _userManager.GetUserId(User)!;

        var attachment = await _db.Attachments
            .Include(a => a.Note)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (attachment is null) return NotFound();

        var note = attachment.Note!;
        bool isOwner = note.OwnerId == userId;
        bool isAdmin = User.IsInRole("Admin");

        if (!isOwner && !isAdmin)
            return Forbid();

        await _fileStorage.DeleteAsync(attachment.StoredFileName);
        _db.Attachments.Remove(attachment);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("AttachmentDeleted", userId, true,
            targetId: id.ToString(), targetType: "Attachment");

        return RedirectToAction("Edit", "Notes", new { id = noteId });
    }
}
