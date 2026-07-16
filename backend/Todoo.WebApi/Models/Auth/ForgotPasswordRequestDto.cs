using System.ComponentModel.DataAnnotations;

namespace Todoo.WebApi.Models.Auth;

public class ForgotPasswordRequestDto
{
    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Gecerli bir e-posta adresi giriniz.")]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;
}
