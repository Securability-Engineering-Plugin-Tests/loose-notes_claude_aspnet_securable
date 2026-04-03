using LooseNotes.Data;
using LooseNotes.Models;
using LooseNotes.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LooseNotes.Controllers;

/// <summary>
/// Administrative dashboard. All actions require the Admin role.
/// SSEM: Accountability — every admin action is logged with user and target.
/// SSEM: Integrity — no command injection; no unauthenticated state-change endpoints.
/// PRD command-execution endpoint is NOT implemented — it is an OS injection vulnerability.
/// PRD "reinitialise DB from user-supplied parameters" is NOT implemented — SSRF/injection risk.
/// </summary>
[Authorize(Roles = "Admin")]
public sealed class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ILogger<AdminController> logger)
    {
        _db = db;
        _userManager = userManager;
        _logger = logger;
    }

    // ── Dashboard ─────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var vm = new AdminDashboardViewModel
        {
            TotalUsers = await _db.Users.CountAsync(),
            TotalNotes = await _db.Notes.CountAsync(),
            RecentUsers = await _db.Users
                .OrderByDescending(u => u.CreatedAt)
                .Take(10)
                .AsNoTracking()
                .ToListAsync()
        };
        return View(vm);
    }

    // ── User List ─────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Users()
    {
        var users = await _db.Users
            .OrderBy(u => u.UserName)
            .AsNoTracking()
            .ToListAsync();

        return View(new UserListViewModel { Users = users });
    }

    // ── Note Ownership Reassignment ───────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> ReassignNote(int id)
    {
        var note = await _db.Notes.FindAsync(id);
        if (note == null) return NotFound();

        var users = await _db.Users.OrderBy(u => u.UserName).AsNoTracking().ToListAsync();

        return View(new ReassignNoteViewModel
        {
            NoteId = id,
            NoteTitle = note.Title,
            Users = users
        });
    }

    [HttpPost]
    public async Task<IActionResult> ReassignNote(ReassignNoteViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var adminId = _userManager.GetUserId(User)!;

        // Validate that target user actually exists
        var targetUser = await _userManager.FindByIdAsync(model.TargetUserId);
        if (targetUser == null)
        {
            ModelState.AddModelError(nameof(model.TargetUserId), "Target user not found");
            model.Users = await _db.Users.OrderBy(u => u.UserName).AsNoTracking().ToListAsync();
            return View(model);
        }

        var note = await _db.Notes.FindAsync(model.NoteId);
        if (note == null) return NotFound();

        var previousOwner = note.OwnerId;
        note.OwnerId = model.TargetUserId;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Admin {AdminId} reassigned note {NoteId} from {PrevOwner} to {NewOwner}",
            adminId, note.Id, previousOwner, model.TargetUserId);

        TempData["Success"] = $"Note '{note.Title}' reassigned to {targetUser.UserName}";
        return RedirectToAction(nameof(Index));
    }
}
