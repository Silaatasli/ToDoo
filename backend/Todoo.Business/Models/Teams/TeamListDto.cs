namespace Todoo.Business.Models.Teams;

public class TeamListDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int LeaderUserId { get; set; }

    public string LeaderEmail { get; set; } = string.Empty;

    public int MemberCount { get; set; }

    public DateTime CreatedDate { get; set; }
}
