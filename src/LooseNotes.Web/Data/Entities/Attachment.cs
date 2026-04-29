using System.ComponentModel.DataAnnotations;

namespace LooseNotes.Web.Data.Entities;

public class Attachment
{
    public int Id { get; set; }

    public int NoteId { get; set; }
    public Note? Note { get; set; }

    // StoredFileName is server-generated (GUID + safe extension). It is the only
    // value used to build a filesystem path; OriginalFileName is kept for display
    // only and is HTML-encoded on render.
    [Required, MaxLength(128)]
    public string StoredFileName { get; set; } = default!;

    [Required, MaxLength(255)]
    public string OriginalFileName { get; set; } = default!;

    [Required, MaxLength(127)]
    public string ContentType { get; set; } = default!;

    public long SizeBytes { get; set; }

    [Required]
    public string OwnerId { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
