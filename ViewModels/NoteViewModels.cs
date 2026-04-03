using LooseNotes.Models;
using System.ComponentModel.DataAnnotations;

namespace LooseNotes.ViewModels;

public sealed class CreateNoteViewModel
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(50000)]
    public string Content { get; set; } = string.Empty;

    public bool IsPublic { get; set; }
}

public sealed class EditNoteViewModel
{
    // Route-bound ID, but ownership is verified server-side — not accepted as authoritative from body
    public int Id { get; set; }

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(50000)]
    public string Content { get; set; } = string.Empty;

    public bool IsPublic { get; set; }
}

public sealed class NoteDetailViewModel
{
    public Note Note { get; set; } = null!;
    public bool IsOwner { get; set; }
    public IList<Rating> Ratings { get; set; } = [];
    public RatingSubmitViewModel NewRating { get; set; } = new();
}

public sealed class RatingSubmitViewModel
{
    public int NoteId { get; set; }

    [Range(1, 5, ErrorMessage = "Score must be between 1 and 5")]
    public int Score { get; set; }

    [StringLength(1000)]
    public string Comment { get; set; } = string.Empty;
}

public sealed class SearchViewModel
{
    [StringLength(200)]
    public string? Keyword { get; set; }
    public IList<Note> Results { get; set; } = [];
}

public sealed class TopRatedViewModel
{
    public IList<Note> Notes { get; set; } = [];
}

public sealed class ExportRequestViewModel
{
    [Required]
    public int[] NoteIds { get; set; } = [];
}

public sealed class NoteListViewModel
{
    public IList<Note> Notes { get; set; } = [];
}
