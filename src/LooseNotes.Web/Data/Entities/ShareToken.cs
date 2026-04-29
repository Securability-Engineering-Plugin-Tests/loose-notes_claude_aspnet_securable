using System.ComponentModel.DataAnnotations;

namespace LooseNotes.Web.Data.Entities;

public class ShareToken
{
    public int Id { get; set; }

    public int NoteId { get; set; }
    public Note? Note { get; set; }

    // 256-bit URL-safe token produced by RandomNumberGenerator.GetBytes (CSPRNG).
    // Stored hashed at rest so a database leak does not surrender shareable URLs.
    [Required, MaxLength(128)]
    public string TokenHash { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
