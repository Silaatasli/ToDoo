using System.Text.Json.Serialization;

namespace Todoo.Entities.Entities;

public class Board
{
    public int Id { get; set; }

    public int TeamId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public Team Team { get; set; } = null!;

    [JsonIgnore]
    public ICollection<TeamBoardColumn> Columns { get; set; } = [];

    [JsonIgnore]
    public ICollection<TaskItem> Tasks { get; set; } = [];

    [JsonIgnore]
    public ICollection<Sprint> Sprints { get; set; } = [];
}
