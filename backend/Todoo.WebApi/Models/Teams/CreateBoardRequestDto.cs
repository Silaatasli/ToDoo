using System.ComponentModel.DataAnnotations;

namespace Todoo.WebApi.Models.Teams;

public class CreateBoardRequestDto
{
    [Required(ErrorMessage = "Pano adi zorunludur.")]
    [MaxLength(200, ErrorMessage = "Pano adi en fazla 200 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;

    public List<string>? ColumnTitles { get; set; }
}
