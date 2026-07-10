using System.Text.Json.Serialization;

namespace Todoo.Entities.Entities;

public class TeamBoardColumn
{
    public int Id { get; set; }

    public int TeamId { get; set; }

    public string Title { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public bool IsCompletedColumn { get; set; }

    [JsonIgnore]
    public Team Team { get; set; } = null!;

    [JsonIgnore]
    public ICollection<TaskItem> Tasks { get; set; } = [];
}
