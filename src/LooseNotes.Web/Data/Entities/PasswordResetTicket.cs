using System.ComponentModel.DataAnnotations;

namespace LooseNotes.Web.Data.Entities;

// Server-side state for the password recovery flow. Step 1 records that an
// answer was demanded for this user (no information is leaked back to the
// client about whether the email exists). Step 2 validates the answer
// server-side using the constant-time hasher; on success we do NOT reveal the
// password — we issue a short-lived reset token and let the user choose a
// new password.
public class PasswordResetTicket
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = default!;

    [Required, MaxLength(128)]
    public string TicketIdHash { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }

    public bool AnswerVerified { get; set; }
    public bool Consumed { get; set; }

    public int FailedAnswerAttempts { get; set; }
}
