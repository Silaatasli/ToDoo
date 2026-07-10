namespace Todoo.Business.Models.Teams;

public class TeamDetailDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int LeaderUserId { get; set; }

    public string LeaderEmail { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; }

    public List<TeamMemberDto> Members { get; set; } = [];

    public List<TeamBoardColumnDto> BoardColumns { get; set; } = [];
}
