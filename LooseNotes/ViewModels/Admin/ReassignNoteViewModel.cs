using System.ComponentModel.DataAnnotations;

namespace LooseNotes.ViewModels.Admin;

public class ReassignNoteViewModel
{
    public int NoteId { get; set; }
    public string NoteTitle { get; set; } = string.Empty;
    public string CurrentOwnerUserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please select a new owner.")]
    [Display(Name = "New Owner")]
    public string NewOwnerId { get; set; } = string.Empty;

    public List<UserOption> AllUsers { get; set; } = new();
}

public class UserOption
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
}
