using LooseNotes.Web.Data;
using LooseNotes.Web.Data.Entities;
using LooseNotes.Web.Options;

namespace LooseNotes.Web.Services;

public interface IAttachmentStorageService
{
    Task<Attachment> SaveAsync(int noteId, string ownerId, IFormFile file, CancellationToken ct);
    Task<(Stream Stream, string ContentType, string DisplayName)?> OpenAsync(int attachmentId, string callerUserId, bool callerIsAdmin, CancellationToken ct);
    string AttachmentsRootFullPath { get; }
}

// Attachment storage is a high-risk boundary (PRD §7, §23). FIASSE controls
// applied here:
//   - Allow-list of file extensions and content-type families.
//   - Server-generated stored filename (GUID + safe extension); client name
//     is preserved only as a display label.
//   - Storage root lives OUTSIDE wwwroot — files are never web-served by
//     directory mapping; downloads go through the controller which checks
//     ownership.
//   - SafePathResolver enforces base-directory containment.
//   - Size limit enforced via Microsoft.AspNetCore form options + here.
public sealed class AttachmentStorageService : IAttachmentStorageService
{
    private static readonly IReadOnlyDictionary<string, string> ExtensionToContentType =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"]  = "application/pdf",
            [".png"]  = "image/png",
            [".jpg"]  = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"]  = "image/gif",
            [".txt"]  = "text/plain; charset=utf-8",
            [".csv"]  = "text/csv; charset=utf-8",
            [".md"]   = "text/markdown; charset=utf-8",
        };

    private readonly AppDbContext _db;
    private readonly ISafePathResolver _paths;
    private readonly StorageOptions _opts;
    private readonly ILogger<AttachmentStorageService> _log;

    public AttachmentStorageService(
        AppDbContext db,
        ISafePathResolver paths,
        StorageOptions opts,
        IWebHostEnvironment env,
        ILogger<AttachmentStorageService> log)
    {
        _db = db;
        _paths = paths;
        _opts = opts;
        _log = log;
        var root = Path.IsPathRooted(opts.AttachmentsRoot)
            ? opts.AttachmentsRoot
            : Path.Combine(env.ContentRootPath, opts.AttachmentsRoot);
        AttachmentsRootFullPath = paths.EnsureBaseDirectory(root);
    }

    public string AttachmentsRootFullPath { get; }

    public async Task<Attachment> SaveAsync(int noteId, string ownerId, IFormFile file, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Length <= 0)
            throw new InvalidAttachmentException("attachment is empty");
        if (file.Length > _opts.MaxAttachmentBytes)
            throw new InvalidAttachmentException("attachment exceeds the maximum allowed size");

        var clientName = Path.GetFileName(file.FileName ?? string.Empty);
        var ext = Path.GetExtension(clientName).ToLowerInvariant();
        if (!ExtensionToContentType.TryGetValue(ext, out var canonicalContentType))
            throw new InvalidAttachmentException("attachment file type is not allowed");

        var stored = $"{Guid.NewGuid():N}{ext}";
        var fullPath = _paths.ResolveUnder(AttachmentsRootFullPath, stored);

        await using (var dest = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await file.CopyToAsync(dest, ct);
        }

        var attachment = new Attachment
        {
            NoteId = noteId,
            OwnerId = ownerId,
            StoredFileName = stored,
            OriginalFileName = TruncateForDisplay(clientName, 200),
            ContentType = canonicalContentType,
            SizeBytes = file.Length
        };
        _db.Attachments.Add(attachment);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "attachment.saved note_id={NoteId} actor={Actor} stored_name={Stored} size_bytes={Size}",
            noteId, ownerId, stored, file.Length);
        return attachment;
    }

    public async Task<(Stream Stream, string ContentType, string DisplayName)?> OpenAsync(
        int attachmentId, string callerUserId, bool callerIsAdmin, CancellationToken ct)
    {
        var att = await _db.Attachments.FindAsync(new object?[] { attachmentId }, ct);
        if (att is null)
        {
            _log.LogInformation("attachment.access.denied id={Id} actor={Actor} reason=not_found",
                attachmentId, callerUserId);
            return null;
        }
        if (att.OwnerId != callerUserId && !callerIsAdmin)
        {
            _log.LogWarning("attachment.access.denied id={Id} actor={Actor} reason=not_owner",
                attachmentId, callerUserId);
            return null;
        }

        var fullPath = _paths.ResolveUnder(AttachmentsRootFullPath, att.StoredFileName);
        if (!File.Exists(fullPath))
        {
            _log.LogWarning("attachment.missing_on_disk id={Id} stored_name={Stored}",
                attachmentId, att.StoredFileName);
            return null;
        }

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        return (stream, att.ContentType, att.OriginalFileName);
    }

    private static string TruncateForDisplay(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max);
}

public sealed class InvalidAttachmentException : Exception
{
    public InvalidAttachmentException(string message) : base(message) { }
}
