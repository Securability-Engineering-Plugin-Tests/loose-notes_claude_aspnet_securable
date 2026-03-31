using Microsoft.AspNetCore.Identity;

namespace LooseNotes.Models;

/// <summary>
/// Extended Identity user with profile fields.
/// Confidentiality: Only non-sensitive profile data stored here; no tokens or secrets.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Note> Notes { get; set; } = new List<Note>();
    public ICollection<Rating> Ratings { get; set; } = new List<Rating>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
