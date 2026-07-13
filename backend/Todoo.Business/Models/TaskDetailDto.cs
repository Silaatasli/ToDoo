using Todoo.Entities.Enums;

namespace Todoo.Business.Models;

public class TaskDetailDto
{
    public int Id { get; set; }

    public int TeamId { get; set; }

    public string? TeamName { get; set; }

    public bool IsPersonalTeam { get; set; }

    public int BoardId { get; set; }

    public string? BoardName { get; set; }

    public int BoardColumnId { get; set; }

    public string BoardColumnTitle { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int? CategoryId { get; set; }

    public string? CategoryName { get; set; }

    public Priority Priority { get; set; }

    public DateTime CreatedDate { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime? DueDate { get; set; }

    public bool IsCompleted { get; set; }

    public int CreatedByUserId { get; set; }

    public string CreatedByEmail { get; set; } = string.Empty;

    public int? AssignedToUserId { get; set; }

    public string? AssignedToEmail { get; set; }

    public AssignmentStatus AssignmentStatus { get; set; }
}
