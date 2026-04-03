using LooseNotes.Models;
using LooseNotes.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace LooseNotes.Controllers;

/// <summary>
/// User profile management.
/// SSEM: Authenticity — identity always resolved from ClaimsPrincipal, never from a cookie value.
/// SSEM: Confidentiality — password hash is never exposed; change requires current password.
/// ASVS: V6.2.3 (current password required for change), V4.2 (no IDOR via cookie).
/// </summary>
[Authorize]
public sealed class ProfileController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<ProfileController> _logger;

    public ProfileController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<ProfileController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
    }

    // ── View Profile ──────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        // Identity resolved from ClaimsPrincipal — never from a request cookie (ASVS V4.2)
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        return View(new ProfileViewModel
        {
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            DisplayName = user.DisplayName,
            CreatedAt = user.CreatedAt
        });
    }

    // ── Edit Profile ──────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Edit()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        return View(new EditProfileViewModel
        {
            DisplayName = user.DisplayName,
            Email = user.Email ?? string.Empty
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(EditProfileViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        user.DisplayName = model.DisplayName;

        if (!string.Equals(user.Email, model.Email, StringComparison.OrdinalIgnoreCase))
        {
            var setEmailResult = await _userManager.SetEmailAsync(user, model.Email);
            if (!setEmailResult.Succeeded)
            {
                foreach (var error in setEmailResult.Errors)
                    ModelState.AddModelError(string.Empty, error.Description);
                return View(model);
            }
        }

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            foreach (var error in updateResult.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        _logger.LogInformation("Profile updated for user {UserId}", user.Id);
        TempData["Success"] = "Profile updated";
        return RedirectToAction(nameof(Index));
    }

    // ── Change Password ───────────────────────────────────────────────────────

    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        // Requires current password — prevents account takeover via CSRF or session fixation
        // ASVS V6.2.3
        var result = await _userManager.ChangePasswordAsync(
            user, model.CurrentPassword, model.NewPassword);

        if (result.Succeeded)
        {
            await _signInManager.RefreshSignInAsync(user);
            _logger.LogInformation("Password changed for user {UserId}", user.Id);
            TempData["Success"] = "Password changed successfully";
            return RedirectToAction(nameof(Index));
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        return View(model);
    }
}
