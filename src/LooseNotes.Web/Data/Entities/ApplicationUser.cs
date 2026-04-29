using Microsoft.AspNetCore.Identity;

namespace LooseNotes.Web.Data.Entities;

// Identity-backed user. Password storage is delegated to ASP.NET Core Identity's
// PasswordHasher (PBKDF2/HMAC-SHA512, 100k iterations as of Identity v3) — never
// reversibly encoded. SecurityAnswerHash is hashed with the same hasher, so the
// answer can be verified but never recovered for display.
public class ApplicationUser : IdentityUser
{
    public string? SecurityQuestionId { get; set; }
    public string? SecurityAnswerHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
