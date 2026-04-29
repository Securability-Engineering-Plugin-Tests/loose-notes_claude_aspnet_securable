using LooseNotes.Web.Models;
using LooseNotes.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LooseNotes.Web.Controllers;

[Route("Recovery")]
[EnableRateLimiting("recovery")]
public class RecoveryController : Controller
{
    private const string TicketCookie = "LooseNotes.Recovery";

    private readonly IPasswordRecoveryService _service;
    private readonly ILogger<RecoveryController> _log;

    public RecoveryController(IPasswordRecoveryService service, ILogger<RecoveryController> log)
    {
        _service = service;
        _log = log;
    }

    [HttpGet("")]
    public IActionResult Begin() => View(new RecoveryStartInput());

    [HttpPost("Begin"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Begin(RecoveryStartInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(input);
        var result = await _service.BeginAsync(input.Email, ct);

        // Generic response regardless of whether the email exists — avoids
        // enumeration. The ticket cookie is only set when the user is real.
        if (result.TicketToken is not null)
        {
            Response.Cookies.Append(TicketCookie, result.TicketToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                IsEssential = true,
                MaxAge = TimeSpan.FromMinutes(30)
            });
        }
        TempData["RecoveryQuestion"] = result.QuestionText
            ?? "If the email address is registered, you'll see your security question on the next page.";
        return RedirectToAction(nameof(Answer));
    }

    [HttpGet("Answer")]
    public IActionResult Answer()
    {
        ViewData["Question"] = TempData["RecoveryQuestion"] as string
            ?? "Answer your security question.";
        TempData.Keep("RecoveryQuestion");
        return View(new RecoveryAnswerInput());
    }

    [HttpPost("Answer"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Answer(RecoveryAnswerInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(input);
        var ticket = Request.Cookies[TicketCookie];
        if (string.IsNullOrEmpty(ticket))
        {
            ModelState.AddModelError(string.Empty, "Recovery session is missing or expired.");
            return View(input);
        }

        var ok = await _service.VerifyAnswerAsync(ticket, input.Answer, ct);
        if (!ok)
        {
            _log.LogInformation("password_recovery.answer_failed");
            ModelState.AddModelError(string.Empty, "We couldn't verify your answer.");
            return View(input);
        }
        return RedirectToAction(nameof(Reset));
    }

    [HttpGet("Reset")]
    public IActionResult Reset() => View(new RecoveryResetInput());

    [HttpPost("Reset"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Reset(RecoveryResetInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(input);
        var ticket = Request.Cookies[TicketCookie];
        if (string.IsNullOrEmpty(ticket))
        {
            ModelState.AddModelError(string.Empty, "Recovery session is missing or expired.");
            return View(input);
        }
        var ok = await _service.ResetPasswordAsync(ticket, input.NewPassword, ct);
        if (!ok)
        {
            ModelState.AddModelError(string.Empty, "Password reset was not accepted.");
            return View(input);
        }
        Response.Cookies.Delete(TicketCookie);
        TempData["RecoverySuccess"] = "Your password was reset. Sign in with the new password.";
        return RedirectToAction("Login", "Account");
    }
}
