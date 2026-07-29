using System.Text.Json.Serialization;
using Todoo.Entities.Enums;

namespace Todoo.Entities.Entities;

public class TaskItem
{
    public int Id { get; set; }

    public int TeamId { get; set; }

    public int BoardId { get; set; }

    public int BoardColumnId { get; set; }

    /// <summary>Sutun icindeki sira (0 tabanli).</summary>
    public int DisplayOrder { get; set; }

    /// <summary>parent taskın idsi Null ise ana gorev(parent yok); dolu ise alt gorev.</summary>
    public int? ParentTaskId { get; set; }

    /// <summary>Sadece alt gorevlerde kullanilir.</summary>
    public SubtaskStatus? SubtaskStatus { get; set; }

    public int CreatedByUserId { get; set; }

    public int? AssignedToUserId { get; set; }

    public AssignmentStatus AssignmentStatus { get; set; }

    public int? CategoryId { get; set; }

    [JsonIgnore]
    public Team Team { get; set; } = null!;

    [JsonIgnore]
    public Board Board { get; set; } = null!;

    [JsonIgnore]
    public TeamBoardColumn BoardColumn { get; set; } = null!;

    [JsonIgnore]
    public TaskItem? ParentTask { get; set; }

    [JsonIgnore]
    public ICollection<TaskItem> Subtasks { get; set; } = [];

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

    /// <summary>Tamamlandi sutununa alindigi an (UTC). SLA hesabi icin.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Null ise backlog; dolu ise ilgili sprint.</summary>
    public int? SprintId { get; set; }

    /// <summary>Sprint veya backlog icindeki sira (0 tabanli).</summary>
    public int SprintOrder { get; set; }

    public DateTime? DeletedAt { get; set; }

    public int? DeletedByUserId { get; set; }

    [JsonIgnore]
    public User? DeletedBy { get; set; }

    [JsonIgnore]
    public Sprint? Sprint { get; set; }

    [JsonIgnore]
    public ICollection<TaskActivityLog> ActivityLogs { get; set; } = [];

    [JsonIgnore]
    public ICollection<TaskAttachment> Attachments { get; set; } = [];

    [JsonIgnore]
    public ICollection<TaskComment> Comments { get; set; } = [];
}
