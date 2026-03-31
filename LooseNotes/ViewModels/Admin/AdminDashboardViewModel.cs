using LooseNotes.Models;

namespace LooseNotes.ViewModels.Admin;

public class AdminDashboardViewModel
{
    public int TotalUsers { get; set; }
    public int TotalNotes { get; set; }
    public List<AuditLog> RecentActivity { get; set; } = new();
}
