using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LooseNotes.Models;

public class Rating
{
    public int Id { get; set; }

    [Range(1, 5)]
    public int Value { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int NoteId { get; set; }

    [ForeignKey(nameof(NoteId))]
    public Note? Note { get; set; }

    // RaterId is server-assigned from authenticated session
    [Required]
    public string RaterId { get; set; } = string.Empty;

    [ForeignKey(nameof(RaterId))]
    public ApplicationUser? Rater { get; set; }
}
