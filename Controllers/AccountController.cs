using LooseNotes.Models;
using LooseNotes.Services;
using LooseNotes.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LooseNotes.Controllers;

/// <summary>
/// Handles registration, login, logout, and password reset.
/// SSEM: Authenticity — uses ASP.NET Core Identity (PBKDF2-SHA256 hashing).
/// SSEM: Accountability — all auth events are logged with structured data.
/// ASVS: V6.2 (password security), V2.2 (brute-force lockout), V3.4 (secure cookies).
/// </summary>
[AllowAnonymous]
public sealed class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IEmailService _emailService;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IEmailService emailService,
        ILogger<AccountController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _emailService = emailService;
        _logger = logger;
    }

    // ── Registration ──────────────────────────────────────────────────────────

    [HttpGet]
    public IActionResult Register() => View();

    [HttpPost]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = new ApplicationUser
        {
            UserName = model.UserName,
            Email = model.Email,
            DisplayName = model.UserName
        };

        // Identity hashes with PBKDF2-SHA256 — no Base64 encoding (ASVS V6.2)
        var result = await _userManager.CreateAsync(user, model.Password);

        if (result.Succeeded)
        {
            _logger.LogInformation("New user registered: {UserId}", user.Id);
            await _signInManager.SignInAsync(user, isPersistent: false);
            return RedirectToAction("Index", "Notes");
        }

        // Translate Identity errors to model-state errors — no internal detail exposed
        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        return View(model);
    }

    // ── Login ─────────────────────────────────────────────────────────────────

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid) return View(model);

        // lockoutOnFailure: true — ASVS V2.2 brute-force protection
        var result = await _signInManager.PasswordSignInAsync(
            model.UserName, model.Password,
            isPersistent: false,  // No 14-day persistent cookies
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            _logger.LogInformation("User {User} logged in", model.UserName);
            return LocalRedirect(
                Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Action("Index", "Notes")!);
        }

        if (result.IsLockedOut)
        {
            _logger.LogWarning("Account locked after failed attempts: {User}", model.UserName);
            ModelState.AddModelError(string.Empty, "Account is temporarily locked. Try again later.");
        }
        else
        {
            // Generic message — do not reveal whether the username exists
            _logger.LogWarning("Failed login attempt for username: {User}", model.UserName);
            ModelState.AddModelError(string.Empty, "Invalid username or password");
        }

        return View(model);
    }

    // ── Logout ────────────────────────────────────────────────────────────────

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var userId = _userManager.GetUserId(User);
        await _signInManager.SignOutAsync();
        _logger.LogInformation("User {UserId} logged out", userId);
        return RedirectToAction("Index", "Home");
    }

    // ── Password Reset — Step 1: Request ─────────────────────────────────────

    [HttpGet]
    public IActionResult ForgotPassword() => View();

    [HttpPost]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);

        // Always redirect to confirmation — do not enumerate accounts (ASVS V2.5)
        if (user != null)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var resetLink = Url.Action("ResetPassword", "Account",
                new { token, email = model.Email }, Request.Scheme)!;

            await _emailService.SendPasswordResetAsync(model.Email, resetLink);
            _logger.LogInformation("Password reset requested for {Email}", model.Email);
        }

        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [HttpGet]
    public IActionResult ForgotPasswordConfirmation() => View();

    // ── Password Reset — Step 2: Reset ───────────────────────────────────────

    [HttpGet]
    public IActionResult ResetPassword(string? token, string? email)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(email))
            return RedirectToAction("Index", "Home");

        return View(new ResetPasswordViewModel { Token = token, Email = email });
    }

    [HttpPost]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null)
            return RedirectToAction(nameof(ResetPasswordConfirmation));

        // Token validation is handled by Identity's data-protection stack (ASVS V2.3)
        var result = await _userManager.ResetPasswordAsync(user, model.Token, model.NewPassword);

        if (result.Succeeded)
        {
            _logger.LogInformation("Password reset completed for user {UserId}", user.Id);
            return RedirectToAction(nameof(ResetPasswordConfirmation));
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        return View(model);
    }

    [HttpGet]
    public IActionResult ResetPasswordConfirmation() => View();

    // ── Access Denied ─────────────────────────────────────────────────────────

    [HttpGet]
    public IActionResult AccessDenied() => View();
}
