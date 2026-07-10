using Todoo.Business.Models;

namespace Todoo.Business.Models.Teams;

public class TeamBoardColumnWithTasksDto
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public bool IsCompletedColumn { get; set; }

    public List<TaskListDto> Tasks { get; set; } = [];
}
