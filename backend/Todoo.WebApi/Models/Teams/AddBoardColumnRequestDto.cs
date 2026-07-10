using System.ComponentModel.DataAnnotations;

namespace Todoo.WebApi.Models.Teams;

public class AddBoardColumnRequestDto
{
    [Required(ErrorMessage = "Sutun basligi zorunludur.")]
    [MaxLength(100, ErrorMessage = "Sutun basligi en fazla 100 karakter olabilir.")]
    public string Title { get; set; } = string.Empty;
}
