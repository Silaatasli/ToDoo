using Todoo.Entities.Enums;

namespace Todoo.Business.Models;

public class TaskListDto
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int? CategoryId { get; set; }

    public string? CategoryName { get; set; }

    public Priority Priority { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime? DueDate { get; set; }

    /// <summary>Tamamlanma / cozulme tarihi (UTC).</summary>
    public DateTime? CompletedAt { get; set; }

    public bool IsCompleted { get; set; }

    public int TeamId { get; set; }

    public string? TeamName { get; set; }

    public bool IsPersonalTeam { get; set; }

    public int BoardId { get; set; }

    public string? BoardName { get; set; }

    public int BoardColumnId { get; set; }

    public int DisplayOrder { get; set; }

    public string? BoardColumnTitle { get; set; }

    public int? AssignedToUserId { get; set; }

    public string? AssignedToEmail { get; set; }

    public AssignmentStatus AssignmentStatus { get; set; }

    public int? ParentTaskId { get; set; }

    public SubtaskStatus? SubtaskStatus { get; set; }

    public int SubtaskDoneCount { get; set; }

    public int SubtaskTotal { get; set; }

    public int? SprintId { get; set; }

    public int SprintOrder { get; set; }
}
