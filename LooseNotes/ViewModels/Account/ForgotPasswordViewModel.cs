using System.ComponentModel.DataAnnotations;

namespace LooseNotes.ViewModels.Account;

public class ForgotPasswordViewModel
{
    [Required, EmailAddress, MaxLength(200)]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;
}
