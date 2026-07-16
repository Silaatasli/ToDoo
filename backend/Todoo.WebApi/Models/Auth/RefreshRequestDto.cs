using System.ComponentModel.DataAnnotations;

namespace Todoo.WebApi.Models.Auth;

public class RefreshRequestDto
{
    [Required(ErrorMessage = "Refresh token zorunludur.")]
    public string RefreshToken { get; set; } = string.Empty;
}
