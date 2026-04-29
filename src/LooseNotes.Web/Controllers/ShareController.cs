using System.Security.Claims;
using LooseNotes.Web.Models;
using LooseNotes.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LooseNotes.Web.Controllers;

public class ShareController : Controller
{
    private readonly IShareTokenService _share;

    public ShareController(IShareTokenService share) => _share = share;

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private bool CurrentUserIsAdmin => User.IsInRole(Seeder.AdminRole);

    [Authorize]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ShareCreateInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest();
        try
        {
            var lifetime = input.LifetimeDays is { } d && d > 0 ? TimeSpan.FromDays(d) : (TimeSpan?)null;
            var token = await _share.CreateAsync(input.NoteId, CurrentUserId, CurrentUserIsAdmin, lifetime, ct);
            var url = Url.Action(nameof(View), "Share", new { token }, Request.Scheme);
            TempData["ShareLink"] = url;
        }
        catch (UnauthorizedAccessException)
        {
            return NotFound();
        }
        return RedirectToAction("Details", "Notes", new { id = input.NoteId });
    }

    // Anonymous read with a token. Token is opaque (not an integer) and only a
    // hash of it lives at rest, so a database leak does not yield URLs.
    [AllowAnonymous]
    [HttpGet("Share/{token}")]
    public async Task<IActionResult> View(string token, CancellationToken ct)
    {
        var note = await _share.ResolveAsync(token, ct);
        if (note is null) return NotFound();
        return View("SharedNote", note);
    }
}
