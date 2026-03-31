using LooseNotes.Data;
using LooseNotes.Models;

namespace LooseNotes.Services;

public class AuditService : IAuditService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AuditService> _logger;

    public AuditService(ApplicationDbContext db, ILogger<AuditService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task LogAsync(
        string action,
        string? userId,
        bool success,
        string? targetId = null,
        string? targetType = null,
        string? details = null,
        string? ipAddress = null)
    {
        // Structured log for observability — no sensitive data
        _logger.LogInformation(
            "Audit {Action} UserId={UserId} TargetId={TargetId} Success={Success}",
            action, userId ?? "anonymous", targetId, success);

        var entry = new AuditLog
        {
            Action = action,
            UserId = userId,
            TargetId = targetId,
            TargetType = targetType,
            Success = success,
            Details = details,
            IpAddress = ipAddress,
            OccurredAt = DateTime.UtcNow
        };

        _db.AuditLogs.Add(entry);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Audit failures should not break the primary flow
            _logger.LogError(ex, "Failed to persist audit log for action {Action}", action);
        }
    }
}
