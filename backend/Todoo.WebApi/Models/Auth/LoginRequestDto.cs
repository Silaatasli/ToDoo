using System.ComponentModel.DataAnnotations;

namespace Todoo.WebApi.Models.Auth;

public class LoginRequestDto
{
    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Gecerli bir e-posta adresi giriniz.")]
    [MaxLength(256, ErrorMessage = "E-posta en fazla 256 karakter olabilir.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Sifre zorunludur.")]
    public string Password { get; set; } = string.Empty;
}
