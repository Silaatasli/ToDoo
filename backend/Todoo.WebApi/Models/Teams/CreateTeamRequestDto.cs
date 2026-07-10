using System.ComponentModel.DataAnnotations;

namespace Todoo.WebApi.Models.Teams;

public class CreateTeamRequestDto
{
    [Required(ErrorMessage = "Takim adi zorunludur.")]
    [MaxLength(200, ErrorMessage = "Takim adi en fazla 200 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;

    public List<string>? ColumnTitles { get; set; }
}
