using LooseNotes.Models;
using LooseNotes.Services;
using LooseNotes.ViewModels.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using LooseNotes.Data;
using Microsoft.EntityFrameworkCore;

namespace LooseNotes.Controllers;

[AllowAnonymous]
public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IAuditService _audit;
    private readonly IEmailService _email;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IAuditService audit,
        IEmailService email,
        ApplicationDbContext db,
        ILogger<AccountController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _audit = audit;
        _email = email;
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Register() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = new ApplicationUser
        {
            UserName = model.UserName,
            Email = model.Email,
            DisplayName = model.UserName,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            await _audit.LogAsync("Register", null, false,
                details: "Registration failed for username " + model.UserName,
                ipAddress: GetClientIp());
            return View(model);
        }

        await _userManager.AddToRoleAsync(user, "User");
        await _audit.LogAsync("Register", user.Id, true,
            targetId: user.Id, targetType: "User", ipAddress: GetClientIp());

        await _signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToAction("Index", "Notes");
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(model);

        var result = await _signInManager.PasswordSignInAsync(
            model.UserName, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            var user = await _userManager.FindByNameAsync(model.UserName);
            if (user is not null)
            {
                user.LastLoginAt = DateTime.UtcNow;
                await _userManager.UpdateAsync(user);
                await _audit.LogAsync("Login", user.Id, true, ipAddress: GetClientIp());
            }

            return RedirectToSafeUrl(returnUrl);
        }

        if (result.IsLockedOut)
        {
            await _audit.LogAsync("LoginLockedOut", null, false,
                details: $"Locked out: {model.UserName}", ipAddress: GetClientIp());
            return RedirectToAction(nameof(Lockout));
        }

        await _audit.LogAsync("LoginFailed", null, false,
            details: $"Failed login for: {model.UserName}", ipAddress: GetClientIp());
        ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        return View(model);
    }

    [HttpGet]
    public IActionResult Lockout() => View();

    [HttpPost, ValidateAntiForgeryToken, Authorize]
    public async Task<IActionResult> Logout()
    {
        var userId = _userManager.GetUserId(User);
        await _signInManager.SignOutAsync();
        await _audit.LogAsync("Logout", userId, true, ipAddress: GetClientIp());
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult ForgotPassword() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);

        // Always show confirmation to prevent user enumeration
        if (user is null)
            return RedirectToAction(nameof(ForgotPasswordConfirmation));

        var rawToken = GenerateSecureToken();
        var tokenHash = HashToken(rawToken);

        var resetToken = new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        _db.PasswordResetTokens.Add(resetToken);
        await _db.SaveChangesAsync();

        var resetLink = Url.Action(nameof(ResetPassword), "Account",
            new { token = rawToken, email = model.Email }, Request.Scheme)!;

        await _email.SendPasswordResetEmailAsync(model.Email, resetLink);
        await _audit.LogAsync("PasswordResetRequested", user.Id, true,
            targetId: user.Id, targetType: "User", ipAddress: GetClientIp());

        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [HttpGet]
    public IActionResult ForgotPasswordConfirmation() => View();

    [HttpGet]
    public IActionResult ResetPassword(string? token, string? email)
    {
        if (token is null || email is null)
            return BadRequest("Invalid reset link.");

        return View(new ResetPasswordViewModel { Token = token, Email = email });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is null)
            return RedirectToAction(nameof(ResetPasswordConfirmation));

        var tokenHash = HashToken(model.Token);
        var storedToken = await _db.PasswordResetTokens
            .FirstOrDefaultAsync(t =>
                t.UserId == user.Id &&
                t.TokenHash == tokenHash &&
                !t.IsUsed &&
                t.ExpiresAt > DateTime.UtcNow);

        if (storedToken is null)
        {
            ModelState.AddModelError(string.Empty, "This reset link is invalid or has expired.");
            await _audit.LogAsync("PasswordResetFailed", user.Id, false,
                details: "Invalid or expired token", ipAddress: GetClientIp());
            return View(model);
        }

        var removeResult = await _userManager.RemovePasswordAsync(user);
        var addResult = await _userManager.AddPasswordAsync(user, model.NewPassword);

        if (!addResult.Succeeded)
        {
            foreach (var error in addResult.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        storedToken.IsUsed = true;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("PasswordReset", user.Id, true,
            targetId: user.Id, targetType: "User", ipAddress: GetClientIp());

        return RedirectToAction(nameof(ResetPasswordConfirmation));
    }

    [HttpGet]
    public IActionResult ResetPasswordConfirmation() => View();

    [HttpGet]
    public IActionResult AccessDenied() => View();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private IActionResult RedirectToSafeUrl(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction("Index", "Notes");
    }

    private string? GetClientIp() =>
        HttpContext.Connection.RemoteIpAddress?.ToString();
}
