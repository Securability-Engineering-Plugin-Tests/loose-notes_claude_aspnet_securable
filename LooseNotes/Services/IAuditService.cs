namespace LooseNotes.Services;

/// <summary>
/// Accountability: structured, append-only audit logging for security-sensitive actions.
/// </summary>
public interface IAuditService
{
    Task LogAsync(
        string action,
        string? userId,
        bool success,
        string? targetId = null,
        string? targetType = null,
        string? details = null,
        string? ipAddress = null);
}
