using System.ComponentModel.DataAnnotations;

namespace LooseNotes.Web.Data.Entities;

public class Note
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = default!;

    // Stored as already-sanitized HTML (see HtmlSanitizationService). Sanitization
    // happens on write so reads can render the value through Html.Raw safely.
    [Required]
    public string SanitizedContent { get; set; } = default!;

    public bool IsPublic { get; set; }

    [Required]
    public string OwnerId { get; set; } = default!;
    public ApplicationUser? Owner { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Attachment> Attachments { get; set; } = new();
    public List<Rating> Ratings { get; set; } = new();
    public List<ShareToken> ShareTokens { get; set; } = new();
}
