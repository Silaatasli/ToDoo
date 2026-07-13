namespace Todoo.Business.Models.Teams;

public class TeamMemberDto
{
    public int UserId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public bool IsLeader { get; set; }

    public DateTime JoinedDate { get; set; }

    public bool HasProfilePhoto { get; set; }
}
