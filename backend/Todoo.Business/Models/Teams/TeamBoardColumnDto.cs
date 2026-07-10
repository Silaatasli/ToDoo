namespace Todoo.Business.Models.Teams;

public class TeamBoardColumnDto
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public bool IsCompletedColumn { get; set; }
}
