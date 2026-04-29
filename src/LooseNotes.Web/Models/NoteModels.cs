using System.ComponentModel.DataAnnotations;

namespace LooseNotes.Web.Models;

public class NoteCreateInput
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = default!;

    [Required, StringLength(50_000)]
    public string Content { get; set; } = default!;

    public bool IsPublic { get; set; }
}

public sealed class NoteEditInput : NoteCreateInput
{
    public int Id { get; set; }
}

public sealed class RatingInput
{
    [Required] public int NoteId { get; set; }
    [Range(1, 5)] public int Score { get; set; }
    [StringLength(1000)] public string? Comment { get; set; }
}

public sealed class ShareCreateInput
{
    [Required] public int NoteId { get; set; }

    [Range(0, 365)]
    public int? LifetimeDays { get; set; }
}

public sealed class ReassignInput
{
    [Required] public int NoteId { get; set; }
    [Required] public string TargetUserId { get; set; } = default!;
}
