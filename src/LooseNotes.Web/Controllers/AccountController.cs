using LooseNotes.Web.Data;
using LooseNotes.Web.Data.Entities;
using LooseNotes.Web.Models;
using LooseNotes.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace LooseNotes.Web.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly ISecurityAnswerHasher _answers;
    private readonly AppDbContext _db;
    private readonly ILogger<AccountController> _log;

    public AccountController(
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        ISecurityAnswerHasher answers,
        AppDbContext db,
        ILogger<AccountController> log)
    {
        _users = users;
        _signIn = signIn;
        _answers = answers;
        _db = db;
        _log = log;
    }

    [HttpGet]
    public async Task<IActionResult> Register(CancellationToken ct)
    {
        await PopulateSecurityQuestionsAsync(ct);
        return View(new RegisterInput());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await PopulateSecurityQuestionsAsync(ct);
            return View(input);
        }

        // Distinguishing "username taken" from "email taken" is part of the
        // user-facing requirement (PRD §1) so we surface both — but only
        // through the bound model state, never via timing or via a different
        // code path.
        if (await _users.FindByNameAsync(input.Username) is not null)
            ModelState.AddModelError(nameof(input.Username), "That username is already taken.");
        if (await _users.FindByEmailAsync(input.Email) is not null)
            ModelState.AddModelError(nameof(input.Email), "That email address is already in use.");

        if (!await _db.SecurityQuestions.AnyAsync(q => q.Id == input.SecurityQuestionId, ct))
            ModelState.AddModelError(nameof(input.SecurityQuestionId), "Pick a security question from the list.");

        if (!ModelState.IsValid)
        {
            await PopulateSecurityQuestionsAsync(ct);
            return View(input);
        }

        var user = new ApplicationUser
        {
            UserName = input.Username,
            Email = input.Email,
            SecurityQuestionId = input.SecurityQuestionId,
            SecurityAnswerHash = _answers.Hash(input.SecurityAnswer)
        };
        var result = await _users.CreateAsync(user, input.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            await PopulateSecurityQuestionsAsync(ct);
            return View(input);
        }
        await _users.AddToRoleAsync(user, Seeder.UserRole);
        _log.LogInformation("account.registered user_id={UserId} username={Username}", user.Id, user.UserName);

        await _signIn.SignInAsync(user, isPersistent: false);
        return RedirectToAction("Index", "Notes");
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl) => View(new LoginInput { });

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginInput input, string? returnUrl, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(input);

        var result = await _signIn.PasswordSignInAsync(input.Username, input.Password,
            isPersistent: false, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            _log.LogInformation("account.login.success username={Username}", input.Username);
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
            return RedirectToAction("Index", "Notes");
        }

        // Always emit one generic message — never confirm whether the username
        // exists or whether lockout is engaged.
        _log.LogInformation("account.login.failed username={Username} locked_out={Locked}",
            input.Username, result.IsLockedOut);
        ModelState.AddModelError(string.Empty, "Sign-in attempt was not accepted.");
        return View(input);
    }

    [Authorize]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signIn.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();

    [Authorize, HttpGet]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        // Identity is taken from the authenticated principal — never from a
        // client-supplied cookie or query parameter (PRD §16 explicitly
        // demanded the latter; rejected).
        var user = await _users.GetUserAsync(User);
        if (user is null) return Forbid();
        return View(user);
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordInput input)
    {
        if (!ModelState.IsValid) return RedirectToAction(nameof(Profile));
        var user = await _users.GetUserAsync(User);
        if (user is null) return Forbid();

        var result = await _users.ChangePasswordAsync(user, input.CurrentPassword, input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            TempData["ChangePasswordError"] = "Password change was not accepted.";
            return RedirectToAction(nameof(Profile));
        }
        _log.LogInformation("account.password_changed user_id={UserId}", user.Id);
        TempData["ChangePasswordSuccess"] = "Password updated.";
        return RedirectToAction(nameof(Profile));
    }

    private async Task PopulateSecurityQuestionsAsync(CancellationToken ct)
    {
        ViewData["SecurityQuestions"] = await _db.SecurityQuestions
            .OrderBy(q => q.Id).ToListAsync(ct);
    }
}
