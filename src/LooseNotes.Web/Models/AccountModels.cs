using System.ComponentModel.DataAnnotations;

namespace LooseNotes.Web.Models;

// Each input model defines exactly the values the controller accepts. Request
// Surface Minimization (FIASSE S4.4.1.1) — we never bind directly to entities.

public sealed class RegisterInput
{
    [Required, StringLength(64, MinimumLength = 3), RegularExpression(@"^[a-zA-Z0-9_.-]+$")]
    public string Username { get; set; } = default!;

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = default!;

    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)]
    public string Password { get; set; } = default!;

    [Required, Compare(nameof(Password)), DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = default!;

    [Required, StringLength(64)]
    public string SecurityQuestionId { get; set; } = default!;

    [Required, StringLength(128, MinimumLength = 2)]
    public string SecurityAnswer { get; set; } = default!;
}

public sealed class LoginInput
{
    [Required, StringLength(64)]
    public string Username { get; set; } = default!;

    [Required, StringLength(128), DataType(DataType.Password)]
    public string Password { get; set; } = default!;
}

public sealed class ChangePasswordInput
{
    [Required, DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = default!;

    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)]
    public string NewPassword { get; set; } = default!;

    [Required, Compare(nameof(NewPassword)), DataType(DataType.Password)]
    public string ConfirmNewPassword { get; set; } = default!;
}

public sealed class RecoveryStartInput
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = default!;
}

public sealed class RecoveryAnswerInput
{
    [Required, StringLength(128)]
    public string Answer { get; set; } = default!;
}

public sealed class RecoveryResetInput
{
    [Required, StringLength(128, MinimumLength = 12), DataType(DataType.Password)]
    public string NewPassword { get; set; } = default!;

    [Required, Compare(nameof(NewPassword)), DataType(DataType.Password)]
    public string ConfirmNewPassword { get; set; } = default!;
}
