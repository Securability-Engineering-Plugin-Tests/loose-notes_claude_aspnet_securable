using LooseNotes.Models;
using System.ComponentModel.DataAnnotations;

namespace LooseNotes.ViewModels;

public sealed class AdminDashboardViewModel
{
    public int TotalUsers { get; set; }
    public int TotalNotes { get; set; }
    public IList<ApplicationUser> RecentUsers { get; set; } = [];
}

public sealed class ReassignNoteViewModel
{
    [Required]
    [Range(1, int.MaxValue)]
    public int NoteId { get; set; }

    [Required]
    public string TargetUserId { get; set; } = string.Empty;

    public IList<ApplicationUser> Users { get; set; } = [];
    public string? NoteTitle { get; set; }
}

public sealed class UserListViewModel
{
    public IList<ApplicationUser> Users { get; set; } = [];
}
