using System.Text.Json.Serialization;
using Todoo.Entities.Enums;

namespace Todoo.Entities.Entities;

public class TaskItem
{
    public int Id { get; set; }

    public int TeamId { get; set; }

    public int BoardColumnId { get; set; }

    public int CreatedByUserId { get; set; }

    public int? AssignedToUserId { get; set; }

    public AssignmentStatus AssignmentStatus { get; set; }

    public int? CategoryId { get; set; }

    [JsonIgnore]
    public Team Team { get; set; } = null!;

    [JsonIgnore]
    public TeamBoardColumn BoardColumn { get; set; } = null!;

    [JsonIgnore]
    public User CreatedBy { get; set; } = null!;

    [JsonIgnore]
    public User? AssignedTo { get; set; }

    [JsonIgnore]
    public Category? Category { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Priority Priority { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime StartDate { get; set; } = DateTime.UtcNow;

    public DateTime? DueDate { get; set; }

    public bool IsCompleted { get; set; }

    [JsonIgnore]
    public ICollection<TaskActivityLog> ActivityLogs { get; set; } = [];

    [JsonIgnore]
    public ICollection<TaskAttachment> Attachments { get; set; } = [];
}
