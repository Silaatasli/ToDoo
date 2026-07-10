using System.ComponentModel.DataAnnotations;

namespace Todoo.WebApi.Models.Teams;

public class AddTeamMemberRequestDto
{
    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Gecerli bir e-posta girin.")]
    public string Email { get; set; } = string.Empty;
}
