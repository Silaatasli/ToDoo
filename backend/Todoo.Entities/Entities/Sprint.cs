using System.Text.Json.Serialization;
using Todoo.Entities.Enums;

namespace Todoo.Entities.Entities;

public class Sprint
{
    public int Id { get; set; }

    public int TeamId { get; set; }

    public int BoardId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Goal { get; set; }

    public SprintStatus Status { get; set; } = SprintStatus.Planned;

    public DateTime PlannedStartDate { get; set; }

    public DateTime PlannedEndDate { get; set; }

    public DateTime? ActualStartDate { get; set; }

    public DateTime? ActualEndDate { get; set; }

    public int DisplayOrder { get; set; }

    public int CreatedByUserId { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public Team Team { get; set; } = null!;

    [JsonIgnore]
    public Board Board { get; set; } = null!;

    [JsonIgnore]
    public User CreatedBy { get; set; } = null!;

    [JsonIgnore]
    public ICollection<TaskItem> Tasks { get; set; } = [];

    [JsonIgnore]
    public ICollection<SprintActivityLog> ActivityLogs { get; set; } = [];
}
