using System.ComponentModel.DataAnnotations;

namespace LooseNotes.Models;

public sealed class Rating
{
    public int Id { get; set; }

    // Score constrained to 1–5 by attribute validation
    [Range(1, 5)]
    public int Score { get; set; }

    // Comment stored and rendered safely via Razor encoding
    [StringLength(1000)]
    public string Comment { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int NoteId { get; set; }
    public Note? Note { get; set; }

    // Server-resolved from ClaimsPrincipal — never from client body
    public string RaterId { get; set; } = string.Empty;
    public ApplicationUser? Rater { get; set; }
}
