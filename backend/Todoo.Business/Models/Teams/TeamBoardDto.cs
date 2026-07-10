using Todoo.Entities.Enums;

namespace Todoo.Business.Models.Teams;

public class TeamBoardDto
{
    public int TeamId { get; set; }

    public string TeamName { get; set; } = string.Empty;

    public List<TeamBoardColumnWithTasksDto> Columns { get; set; } = [];
}
