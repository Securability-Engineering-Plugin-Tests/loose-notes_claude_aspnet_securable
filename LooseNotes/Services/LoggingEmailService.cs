namespace LooseNotes.Services;

/// <summary>
/// Development stub — logs email content instead of sending.
/// Replace with SMTP/SendGrid implementation for production.
/// Confidentiality: reset link is logged at debug level only — not at info.
/// </summary>
public class LoggingEmailService : IEmailService
{
    private readonly ILogger<LoggingEmailService> _logger;

    public LoggingEmailService(ILogger<LoggingEmailService> logger)
    {
        _logger = logger;
    }

    public Task SendPasswordResetEmailAsync(
        string toEmail,
        string resetLink,
        CancellationToken ct = default)
    {
        // Debug only — do not log reset links at Info or above in production
        _logger.LogDebug("Password reset email to {Email}. Link: {Link}", toEmail, resetLink);
        _logger.LogInformation("Password reset email dispatched to {Email}", toEmail);
        return Task.CompletedTask;
    }
}
