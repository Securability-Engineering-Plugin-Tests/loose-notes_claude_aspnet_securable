using System.ComponentModel.DataAnnotations;

namespace LooseNotes.ViewModels.Notes;

public class EditNoteViewModel
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Content")]
    public string Content { get; set; } = string.Empty;

    [Display(Name = "Make Public")]
    public bool IsPublic { get; set; }

    [Display(Name = "Add Attachment")]
    public IFormFile? NewAttachment { get; set; }
}
