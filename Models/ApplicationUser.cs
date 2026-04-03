using Microsoft.AspNetCore.Identity;

namespace LooseNotes.Models;

/// <summary>
/// Extended Identity user — adds profile fields while preserving
/// Identity's secure password hashing and session management.
/// Passwords are never stored in this model; Identity manages them via PasswordHash.
/// </summary>
public sealed class ApplicationUser : IdentityUser
{
    // Display name separate from username to avoid leaking login handles
    public string DisplayName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Note> Notes { get; set; } = new List<Note>();
    public ICollection<Rating> Ratings { get; set; } = new List<Rating>();
}
