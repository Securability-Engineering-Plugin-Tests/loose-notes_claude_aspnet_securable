using LooseNotes.Data;
using LooseNotes.Models;
using LooseNotes.Services;
using LooseNotes.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LooseNotes.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditService _audit;

    public AdminController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IAuditService audit)
    {
        _db = db;
        _userManager = userManager;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> Dashboard()
    {
        var vm = new AdminDashboardViewModel
        {
            TotalUsers = await _db.Users.CountAsync(),
            TotalNotes = await _db.Notes.CountAsync(),
            RecentActivity = await _db.AuditLogs
                .Include(a => a.User)
                .OrderByDescending(a => a.OccurredAt)
                .Take(20)
                .ToListAsync()
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Users(string? q)
    {
        var query = _db.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(u =>
                u.UserName!.ToLower().Contains(term) ||
                u.Email!.ToLower().Contains(term));
        }

        var users = await query.ToListAsync();
        var vm = new UserListViewModel { SearchQuery = q };

        foreach (var user in users)
        {
            var noteCount = await _db.Notes.CountAsync(n => n.OwnerId == user.Id);
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

            vm.Users.Add(new UserSummary
            {
                Id = user.Id,
                UserName = user.UserName!,
                Email = user.Email!,
                CreatedAt = user.CreatedAt,
                NoteCount = noteCount,
                IsAdmin = isAdmin
            });
        }

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> ReassignNote(int noteId)
    {
        var note = await _db.Notes
            .Include(n => n.Owner)
            .FirstOrDefaultAsync(n => n.Id == noteId);

        if (note is null) return NotFound();

        var allUsers = await _db.Users
            .Select(u => new UserOption { Id = u.Id, UserName = u.UserName! })
            .ToListAsync();

        return View(new ReassignNoteViewModel
        {
            NoteId = note.Id,
            NoteTitle = note.Title,
            CurrentOwnerUserName = note.Owner?.UserName ?? "Unknown",
            AllUsers = allUsers
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReassignNote(ReassignNoteViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.AllUsers = await _db.Users
                .Select(u => new UserOption { Id = u.Id, UserName = u.UserName! })
                .ToListAsync();
            return View(model);
        }

        var note = await _db.Notes.FindAsync(model.NoteId);
        if (note is null) return NotFound();

        // Validate new owner exists — never trust client-supplied user IDs blindly
        var newOwner = await _userManager.FindByIdAsync(model.NewOwnerId);
        if (newOwner is null)
        {
            ModelState.AddModelError(nameof(model.NewOwnerId), "Selected user does not exist.");
            model.AllUsers = await _db.Users
                .Select(u => new UserOption { Id = u.Id, UserName = u.UserName! })
                .ToListAsync();
            return View(model);
        }

        var adminId = _userManager.GetUserId(User)!;
        var previousOwnerId = note.OwnerId;

        note.OwnerId = newOwner.Id;
        note.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        await _audit.LogAsync("NoteReassigned", adminId, true,
            targetId: note.Id.ToString(), targetType: "Note",
            details: $"Owner changed from {previousOwnerId} to {newOwner.Id}");

        return RedirectToAction(nameof(Dashboard));
    }
}
