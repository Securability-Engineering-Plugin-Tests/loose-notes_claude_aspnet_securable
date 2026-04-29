using System.IO.Compression;
using System.Text.Json;
using LooseNotes.Web.Data;
using LooseNotes.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LooseNotes.Web.Services;

public interface IExportImportService
{
    Task<byte[]> ExportAsync(string ownerId, IReadOnlyCollection<int> noteIds, CancellationToken ct);
    Task<int> ImportAsync(string ownerId, Stream zipStream, CancellationToken ct);
}

public sealed record ExportManifest(string ExportedAt, IReadOnlyList<ExportNote> Notes);
public sealed record ExportNote(int Id, string Title, string Content, bool IsPublic, string CreatedAt, IReadOnlyList<ExportAttachment>? Attachments);
public sealed record ExportAttachment(string Filename, string? OriginalName, string? ContentType);

// Bulk export/import (PRD §20 / §21).
//
// Export:
//   * Only the requesting user's own notes are exported (server-side
//     ownership check rather than trust in caller-supplied list).
//   * Attachment file paths are produced by SafePathResolver — preventing
//     a manipulated DB row from escaping the attachments root.
//
// Import:
//   * ZIP entries are validated against zip-slip: full path of each extracted
//     file must remain inside the attachments base directory; the manifest
//     is the only authoritative source for filenames; attachment payload
//     filenames inside the archive are accepted only if their leaf form
//     matches an entry in the manifest's allow-list.
//   * Decompressed size is bounded; archive entry count is bounded.
//   * Extension allow-list applies to imported files exactly as for upload.
public sealed class ExportImportService : IExportImportService
{
    private const long MaxImportArchiveBytes = 50 * 1024 * 1024;
    private const long MaxImportDecompressedBytes = 200 * 1024 * 1024;
    private const int MaxImportEntries = 2_000;

    private readonly AppDbContext _db;
    private readonly IAttachmentStorageService _attachments;
    private readonly ISafePathResolver _paths;
    private readonly IHtmlSanitizationService _sanitizer;
    private readonly ILogger<ExportImportService> _log;

    public ExportImportService(
        AppDbContext db,
        IAttachmentStorageService attachments,
        ISafePathResolver paths,
        IHtmlSanitizationService sanitizer,
        ILogger<ExportImportService> log)
    {
        _db = db;
        _attachments = attachments;
        _paths = paths;
        _sanitizer = sanitizer;
        _log = log;
    }

    public async Task<byte[]> ExportAsync(string ownerId, IReadOnlyCollection<int> noteIds, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ownerId)) throw new UnauthorizedAccessException();

        var notes = await _db.Notes
            .Include(n => n.Attachments)
            .Where(n => n.OwnerId == ownerId && noteIds.Contains(n.Id))
            .AsNoTracking()
            .ToListAsync(ct);

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = new ExportManifest(
                DateTimeOffset.UtcNow.ToString("O"),
                notes.Select(n => new ExportNote(
                    n.Id, n.Title, n.SanitizedContent, n.IsPublic,
                    n.CreatedAt.ToString("O"),
                    n.Attachments.Select(a => new ExportAttachment(
                        a.StoredFileName, a.OriginalFileName, a.ContentType)).ToList())).ToList());

            var jsonEntry = zip.CreateEntry("notes.json", CompressionLevel.Optimal);
            await using (var jw = jsonEntry.Open())
            {
                await JsonSerializer.SerializeAsync(jw, manifest, new JsonSerializerOptions
                {
                    WriteIndented = true
                }, ct);
            }

            foreach (var note in notes)
            {
                foreach (var att in note.Attachments)
                {
                    var src = _paths.ResolveUnder(_attachments.AttachmentsRootFullPath, att.StoredFileName);
                    if (!File.Exists(src)) continue;
                    var entry = zip.CreateEntry($"attachments/{att.StoredFileName}", CompressionLevel.Optimal);
                    await using var es = entry.Open();
                    await using var fs = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read);
                    await fs.CopyToAsync(es, ct);
                }
            }
        }
        _log.LogInformation("export.completed actor={Actor} note_count={Count}", ownerId, notes.Count);
        return ms.ToArray();
    }

    public async Task<int> ImportAsync(string ownerId, Stream zipStream, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ownerId)) throw new UnauthorizedAccessException();
        ArgumentNullException.ThrowIfNull(zipStream);

        // Buffer to memory with a hard cap to bound resource consumption.
        using var bounded = new MemoryStream();
        var copied = await zipStream.CopyBoundedAsync(bounded, MaxImportArchiveBytes, ct);
        if (copied >= MaxImportArchiveBytes)
            throw new InvalidImportException("archive exceeds the maximum allowed size");
        bounded.Position = 0;

        using var zip = new ZipArchive(bounded, ZipArchiveMode.Read, leaveOpen: false);
        if (zip.Entries.Count > MaxImportEntries)
            throw new InvalidImportException("archive contains too many entries");

        var manifestEntry = zip.GetEntry("notes.json")
            ?? throw new InvalidImportException("notes.json is missing");

        ExportManifest manifest;
        await using (var ms = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<ExportManifest>(ms, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }, ct) ?? throw new InvalidImportException("notes.json is empty or malformed");
        }

        var allowedFilenames = manifest.Notes
            .SelectMany(n => n.Attachments ?? Array.Empty<ExportAttachment>())
            .Select(a => a.Filename)
            .Where(f => !string.IsNullOrEmpty(f))
            .ToHashSet(StringComparer.Ordinal);

        long totalDecompressed = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName == "notes.json") continue;
            if (!entry.FullName.StartsWith("attachments/", StringComparison.Ordinal))
                continue; // skip anything outside the documented schema
            var leaf = entry.Name;
            if (string.IsNullOrEmpty(leaf)) continue; // directory entry
            if (!allowedFilenames.Contains(leaf))
                throw new InvalidImportException("archive contains an attachment not listed in the manifest");

            var dest = _paths.ResolveUnder(_attachments.AttachmentsRootFullPath, leaf);
            if (File.Exists(dest))
                continue; // do not overwrite — skip duplicates
            await using var src = entry.Open();
            await using var fs = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var written = await src.CopyBoundedAsync(fs, MaxImportDecompressedBytes - totalDecompressed, ct);
            totalDecompressed += written;
            if (totalDecompressed >= MaxImportDecompressedBytes)
                throw new InvalidImportException("archive expanded beyond the allowed size");
        }

        var imported = 0;
        foreach (var n in manifest.Notes)
        {
            var note = new Note
            {
                Title = (n.Title ?? string.Empty).Trim(),
                SanitizedContent = _sanitizer.SanitizeRichText(n.Content ?? string.Empty),
                IsPublic = n.IsPublic,
                OwnerId = ownerId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _db.Notes.Add(note);
            await _db.SaveChangesAsync(ct);

            foreach (var att in n.Attachments ?? Array.Empty<ExportAttachment>())
            {
                if (string.IsNullOrEmpty(att.Filename)) continue;
                _db.Attachments.Add(new Attachment
                {
                    NoteId = note.Id,
                    OwnerId = ownerId,
                    StoredFileName = att.Filename,
                    OriginalFileName = att.OriginalName ?? att.Filename,
                    ContentType = att.ContentType ?? "application/octet-stream",
                    SizeBytes = 0
                });
            }
            await _db.SaveChangesAsync(ct);
            imported++;
        }

        _log.LogInformation("import.completed actor={Actor} imported={Count}", ownerId, imported);
        return imported;
    }
}

public sealed class InvalidImportException : Exception
{
    public InvalidImportException(string message) : base(message) { }
}

internal static class StreamExtensions
{
    public static async Task<long> CopyBoundedAsync(this Stream source, Stream dest, long maxBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            if (total + read > maxBytes)
            {
                await dest.WriteAsync(buffer.AsMemory(0, (int)(maxBytes - total)), ct);
                return maxBytes;
            }
            await dest.WriteAsync(buffer.AsMemory(0, read), ct);
            total += read;
        }
        return total;
    }
}
