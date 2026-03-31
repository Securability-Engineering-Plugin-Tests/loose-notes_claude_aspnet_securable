using System.ComponentModel.DataAnnotations;

namespace LooseNotes.ViewModels.Notes;

public class NoteSearchViewModel
{
    [MaxLength(200)]
    [Display(Name = "Search")]
    public string? Query { get; set; }

    public List<NoteListItemViewModel> Results { get; set; } = new();
}
