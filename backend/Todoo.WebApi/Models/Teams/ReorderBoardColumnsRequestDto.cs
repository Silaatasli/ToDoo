using System.ComponentModel.DataAnnotations;

namespace Todoo.WebApi.Models.Teams;

public class ReorderBoardColumnsRequestDto
{
    [Required(ErrorMessage = "Sutun listesi zorunludur.")]
    public List<int> ColumnIds { get; set; } = [];
}
