using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LooseNotes.Models;

/// <summary>
/// Append-only audit record. Never update or delete rows from this table.
/// Accountability: records who, what, when, and outcome for security-sensitive actions.
/// </summary>
public class AuditLog
{
    public long Id { get; set; }

    [Required, MaxLength(100)]
    public string Action { get; set; } = string.Empty;

    // UserId may be null for unauthenticated events (e.g., failed login)
    public string? UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public ApplicationUser? User { get; set; }

    [MaxLength(200)]
    public string? TargetId { get; set; }

    [MaxLength(100)]
    public string? TargetType { get; set; }

    public bool Success { get; set; }

    [MaxLength(500)]
    public string? Details { get; set; }

    // No sensitive data logged
    [MaxLength(45)]
    public string? IpAddress { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
