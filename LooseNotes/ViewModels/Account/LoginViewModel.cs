using System.ComponentModel.DataAnnotations;

namespace LooseNotes.ViewModels.Account;

public class LoginViewModel
{
    [Required, MaxLength(200)]
    [Display(Name = "Username or Email")]
    public string UserName { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }
}
