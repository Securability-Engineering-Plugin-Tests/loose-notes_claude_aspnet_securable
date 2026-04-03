namespace LooseNotes.Services;

/// <summary>
/// Development-only email service that logs the reset link instead of sending.
/// Replace with a real SMTP/SendGrid/SES implementation for production.
/// SSEM: Modifiability — interface boundary allows substitution without changing callers.
/// </summary>
public sealed class LoggingEmailService : IEmailService
{
    private readonly ILogger<LoggingEmailService> _logger;

    public LoggingEmailService(ILogger<LoggingEmailService> logger)
        => _logger = logger;

    public Task SendPasswordResetAsync(string toEmail, string resetLink)
    {
        // Log at Information so it is visible in dev, but do NOT log the link in production.
        // In production, replace this body with a real mailer.
        _logger.LogInformation(
            "[DEV] Password reset link for {Email}: {Link}", toEmail, resetLink);
        return Task.CompletedTask;
    }
}
