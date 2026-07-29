using System.Text.Json.Serialization;
using Todoo.Entities.Enums;

namespace Todoo.Entities.Entities;

public class SprintActivityLog
{
    public int Id { get; set; }

    public int TeamId { get; set; }

    public int SprintId { get; set; }

    public int? TaskId { get; set; }

    public int UserId { get; set; }

    public SprintActivityAction ActionType { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public Team Team { get; set; } = null!;

    [JsonIgnore]
    public Sprint Sprint { get; set; } = null!;

    [JsonIgnore]
    public TaskItem? Task { get; set; }

    [JsonIgnore]
    public User User { get; set; } = null!;
}
