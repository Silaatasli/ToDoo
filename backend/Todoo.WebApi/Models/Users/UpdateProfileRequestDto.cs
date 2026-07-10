using System.ComponentModel.DataAnnotations;

namespace Todoo.WebApi.Models.Users;

public class UpdateProfileRequestDto
{
    [MaxLength(100, ErrorMessage = "Ad en fazla 100 karakter olabilir.")]
    public string? FirstName { get; set; }

    [MaxLength(100, ErrorMessage = "Soyad en fazla 100 karakter olabilir.")]
    public string? LastName { get; set; }

    [MaxLength(30, ErrorMessage = "Telefon en fazla 30 karakter olabilir.")]
    [RegularExpression(@"^\+?[0-9\s\-()]{10,20}$", ErrorMessage = "Gecerli bir telefon numarasi girin.")]
    public string? PhoneNumber { get; set; }

    [MaxLength(100, ErrorMessage = "Unvan en fazla 100 karakter olabilir.")]
    public string? Title { get; set; }
}
