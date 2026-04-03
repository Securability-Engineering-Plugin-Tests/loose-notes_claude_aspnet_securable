using System.ComponentModel.DataAnnotations;

namespace LooseNotes.Models;

public sealed class Attachment
{
    public int Id { get; set; }

    // Server-assigned safe name (GUID + allowed extension)
    [Required]
    [StringLength(260)]
    public string StoredFileName { get; set; } = string.Empty;

    // Original client-supplied name stored only for display — never used in path operations
    [StringLength(260)]
    public string OriginalFileName { get; set; } = string.Empty;

    [StringLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public int NoteId { get; set; }
    public Note? Note { get; set; }
}
