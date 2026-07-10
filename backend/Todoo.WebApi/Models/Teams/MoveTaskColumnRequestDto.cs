using System.ComponentModel.DataAnnotations;

namespace Todoo.WebApi.Models.Teams;

public class MoveTaskColumnRequestDto
{
    [Required(ErrorMessage = "BoardColumnId zorunludur.")]
    public int BoardColumnId { get; set; }
}
