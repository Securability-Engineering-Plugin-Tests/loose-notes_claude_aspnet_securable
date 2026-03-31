namespace LooseNotes.ViewModels.Admin;

public class UserListViewModel
{
    public string? SearchQuery { get; set; }
    public List<UserSummary> Users { get; set; } = new();
}

public class UserSummary
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int NoteCount { get; set; }
    public bool IsAdmin { get; set; }
}
