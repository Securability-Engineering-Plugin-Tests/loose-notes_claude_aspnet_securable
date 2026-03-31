using LooseNotes.Models;

namespace LooseNotes.ViewModels.Notes;

public class NoteDetailsViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string OwnerUserName { get; set; } = string.Empty;
    public bool IsOwner { get; set; }
    public bool IsAdmin { get; set; }
    public List<Attachment> Attachments { get; set; } = new();
    public List<Rating> Ratings { get; set; } = new();
    public double AverageRating { get; set; }
    public int? CurrentUserRatingId { get; set; }
    public int? CurrentUserRatingValue { get; set; }
    public List<ShareLink> ActiveShareLinks { get; set; } = new();
}
