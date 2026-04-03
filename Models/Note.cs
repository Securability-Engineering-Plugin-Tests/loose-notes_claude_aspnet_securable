using System.ComponentModel.DataAnnotations;

namespace LooseNotes.Models;

public sealed class Note
{
    public int Id { get; set; }

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(50000)]
    public string Content { get; set; } = string.Empty;

    public bool IsPublic { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Server-owned: never accepted from client input
    public string OwnerId { get; set; } = string.Empty;
    public ApplicationUser? Owner { get; set; }

    // Share token — set only via ShareController using CSPRNG
    public string? ShareToken { get; set; }

    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
    public ICollection<Rating> Ratings { get; set; } = new List<Rating>();
}
