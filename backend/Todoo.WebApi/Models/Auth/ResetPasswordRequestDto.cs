using System.ComponentModel.DataAnnotations;

namespace Todoo.WebApi.Models.Auth;

public class ResetPasswordRequestDto
{
    [Required(ErrorMessage = "Token zorunludur.")]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Yeni sifre zorunludur.")]
    [MinLength(6, ErrorMessage = "Yeni sifre en az 6 karakter olmalidir.")]
    public string NewPassword { get; set; } = string.Empty;
}
