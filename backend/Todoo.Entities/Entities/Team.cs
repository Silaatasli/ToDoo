using System.Text.Json.Serialization;

namespace Todoo.Entities.Entities;

public class Team
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int LeaderUserId { get; set; }

    public int CreatedByUserId { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public bool IsPersonal { get; set; }

    [JsonIgnore]
    public User Leader { get; set; } = null!;

    [JsonIgnore]
    public User CreatedBy { get; set; } = null!;

    [JsonIgnore]
    public ICollection<TeamMember> Members { get; set; } = [];

    [JsonIgnore]
    public ICollection<TeamBoardColumn> BoardColumns { get; set; } = [];

    [JsonIgnore]
    public ICollection<TaskItem> Tasks { get; set; } = [];

    [JsonIgnore]
    public ICollection<TaskActivityLog> ActivityLogs { get; set; } = [];
}
