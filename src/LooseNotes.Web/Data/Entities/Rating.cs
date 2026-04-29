using System.ComponentModel.DataAnnotations;

namespace LooseNotes.Web.Data.Entities;

public class Rating
{
    public int Id { get; set; }

    public int NoteId { get; set; }
    public Note? Note { get; set; }

    [Required]
    public string SubmitterId { get; set; } = default!;
    public ApplicationUser? Submitter { get; set; }

    [Range(1, 5)]
    public int Score { get; set; }

    // Comment is stored as plain text and HTML-encoded on render. We do not
    // accept HTML in comments — only sanitized rich-text in note bodies.
    [MaxLength(1000)]
    public string? Comment { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
