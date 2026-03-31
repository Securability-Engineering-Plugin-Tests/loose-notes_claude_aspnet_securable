using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LooseNotes.Models;

public class ShareLink
{
    public int Id { get; set; }

    // Cryptographically random token — generated server-side only
    [Required, MaxLength(128)]
    public string Token { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRevoked { get; set; } = false;

    public int NoteId { get; set; }

    [ForeignKey(nameof(NoteId))]
    public Note? Note { get; set; }
}
