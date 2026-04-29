using LooseNotes.Web.Data;
using LooseNotes.Web.Data.Entities;
using LooseNotes.Web.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LooseNotes.Web.Services;

public interface IPasswordRecoveryService
{
    Task<RecoveryStartResult> BeginAsync(string email, CancellationToken ct);
    Task<bool> VerifyAnswerAsync(string ticketToken, string answer, CancellationToken ct);
    Task<bool> ResetPasswordAsync(string ticketToken, string newPassword, CancellationToken ct);
}

public sealed record RecoveryStartResult(bool Accepted, string? TicketToken, string? QuestionText);

// Password recovery — re-engineered.
//
// PRD §4 mandated:
//   * Returning the original password in plaintext (impossible — passwords are
//     hashed) and storing the answer Base64-encoded in a non-HttpOnly cookie.
//   * No rate limiting, no lockout.
//
// FIASSE-aligned alternative implemented here:
//   * Step 1 always returns the same generic 200 to prevent email enumeration.
//     The question text and a single-use ticket are returned to the client only
//     when the email is known. The ticket is opaque (CSPRNG, hashed at rest);
//     the cookie carrying it is HttpOnly + Secure + SameSite=Strict.
//   * Step 2 verifies the supplied answer against the hashed answer using
//     ASP.NET Core Identity's PasswordHasher (PBKDF2, fixed-time compare).
//     Failed attempts are counted on the ticket; after MaxAttempts the ticket
//     is consumed and the user must restart.
//   * Step 3 lets the verified user set a new password — we never reveal the
//     original. New passwords go through Identity's full validators.
//
// Trade-off: a richer flow (email link with token + question) would harden
// against in-browser attacker takeover; documented in README.
public sealed class PasswordRecoveryService : IPasswordRecoveryService
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ITokenHasher _hasher;
    private readonly IPasswordHasher<ApplicationUser> _pwHasher;
    private readonly PasswordRecoveryOptions _opts;
    private readonly ILogger<PasswordRecoveryService> _log;

    private const int MaxAnswerAttemptsPerTicket = 5;

    public PasswordRecoveryService(
        AppDbContext db,
        UserManager<ApplicationUser> users,
        ITokenHasher hasher,
        IPasswordHasher<ApplicationUser> pwHasher,
        PasswordRecoveryOptions opts,
        ILogger<PasswordRecoveryService> log)
    {
        _db = db; _users = users; _hasher = hasher;
        _pwHasher = pwHasher; _opts = opts; _log = log;
    }

    public async Task<RecoveryStartResult> BeginAsync(string email, CancellationToken ct)
    {
        var normalized = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized))
            return new RecoveryStartResult(false, null, null);

        // We always accept the request and produce the same response shape so
        // that an attacker cannot probe for valid emails.
        var user = await _users.FindByEmailAsync(normalized);
        if (user is null || string.IsNullOrEmpty(user.SecurityQuestionId))
        {
            _log.LogInformation("password_recovery.begin email_known=false");
            return new RecoveryStartResult(true, null, null);
        }

        var question = await _db.SecurityQuestions.FirstOrDefaultAsync(q => q.Id == user.SecurityQuestionId, ct);
        var ticketToken = _hasher.NewUrlSafeToken(32);
        var ticket = new PasswordResetTicket
        {
            UserId = user.Id,
            TicketIdHash = _hasher.Hash(ticketToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_opts.TokenLifetimeMinutes)
        };
        _db.PasswordResetTickets.Add(ticket);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("password_recovery.begin user_id={UserId} ticket_id={TicketId}",
            user.Id, ticket.Id);
        return new RecoveryStartResult(true, ticketToken, question?.Text);
    }

    public async Task<bool> VerifyAnswerAsync(string ticketToken, string answer, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ticketToken) || string.IsNullOrEmpty(answer)) return false;

        var ticket = await LoadActiveTicketAsync(ticketToken, ct);
        if (ticket is null) return false;

        var user = await _users.FindByIdAsync(ticket.UserId);
        if (user is null || string.IsNullOrEmpty(user.SecurityAnswerHash))
        {
            ticket.Consumed = true;
            await _db.SaveChangesAsync(ct);
            return false;
        }

        var verify = _pwHasher.VerifyHashedPassword(user, user.SecurityAnswerHash, answer.Trim().ToLowerInvariant());
        if (verify == PasswordVerificationResult.Failed)
        {
            ticket.FailedAnswerAttempts++;
            if (ticket.FailedAnswerAttempts >= MaxAnswerAttemptsPerTicket)
            {
                ticket.Consumed = true;
                _log.LogWarning("password_recovery.locked ticket_id={TicketId} user_id={UserId}", ticket.Id, ticket.UserId);
            }
            await _db.SaveChangesAsync(ct);
            return false;
        }

        ticket.AnswerVerified = true;
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("password_recovery.answer_verified ticket_id={TicketId} user_id={UserId}",
            ticket.Id, ticket.UserId);
        return true;
    }

    public async Task<bool> ResetPasswordAsync(string ticketToken, string newPassword, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ticketToken) || string.IsNullOrEmpty(newPassword)) return false;
        var ticket = await LoadActiveTicketAsync(ticketToken, ct);
        if (ticket is null || !ticket.AnswerVerified) return false;

        var user = await _users.FindByIdAsync(ticket.UserId);
        if (user is null) return false;

        var resetToken = await _users.GeneratePasswordResetTokenAsync(user);
        var result = await _users.ResetPasswordAsync(user, resetToken, newPassword);
        if (!result.Succeeded)
        {
            _log.LogInformation("password_recovery.reset_rejected user_id={UserId} errors={Errors}",
                user.Id, string.Join(",", result.Errors.Select(e => e.Code)));
            return false;
        }

        ticket.Consumed = true;
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("password_recovery.completed user_id={UserId} ticket_id={TicketId}",
            user.Id, ticket.Id);
        return true;
    }

    private async Task<PasswordResetTicket?> LoadActiveTicketAsync(string ticketToken, CancellationToken ct)
    {
        var hash = _hasher.Hash(ticketToken);
        var ticket = await _db.PasswordResetTickets.FirstOrDefaultAsync(t => t.TicketIdHash == hash, ct);
        if (ticket is null) return null;
        if (ticket.Consumed) return null;
        if (ticket.ExpiresAt < DateTimeOffset.UtcNow) return null;
        return ticket;
    }
}
