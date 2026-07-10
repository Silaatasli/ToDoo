using System.ComponentModel.DataAnnotations;

namespace Todoo.WebApi.Models.Auth;

public class RegisterRequestDto
{
    [Required(ErrorMessage = "Ad zorunludur.")]
    [MaxLength(100, ErrorMessage = "Ad en fazla 100 karakter olabilir.")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Soyad zorunludur.")]
    [MaxLength(100, ErrorMessage = "Soyad en fazla 100 karakter olabilir.")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Gecerli bir e-posta adresi giriniz.")]
    [MaxLength(256, ErrorMessage = "E-posta en fazla 256 karakter olabilir.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Sifre zorunludur.")]
    [MinLength(6, ErrorMessage = "Sifre en az 6 karakter olmalidir.")]
    public string Password { get; set; } = string.Empty;
}
