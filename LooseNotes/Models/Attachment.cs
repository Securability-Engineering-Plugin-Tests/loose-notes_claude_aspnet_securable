using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LooseNotes.Models;

public class Attachment
{
    public int Id { get; set; }

    // Stored filename (UUID-based, safe for filesystem)
    [Required, MaxLength(256)]
    public string StoredFileName { get; set; } = string.Empty;

    // Original filename for display only — never used for filesystem operations
    [Required, MaxLength(256)]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public int NoteId { get; set; }

    [ForeignKey(nameof(NoteId))]
    public Note? Note { get; set; }

    // Track uploader for accountability
    [Required]
    public string UploadedById { get; set; } = string.Empty;

    [ForeignKey(nameof(UploadedById))]
    public ApplicationUser? UploadedBy { get; set; }
}
