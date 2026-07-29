using Todoo.Entities.Enums;

namespace Todoo.Business.Models.Sprints;

public class SprintListItemDto
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public int BoardId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Goal { get; set; }
    public SprintStatus Status { get; set; }
    public DateTime PlannedStartDate { get; set; }
    public DateTime PlannedEndDate { get; set; }
    public DateTime? ActualStartDate { get; set; }
    public DateTime? ActualEndDate { get; set; }
    public int DisplayOrder { get; set; }
    public int TaskCount { get; set; }
    public int CompletedTaskCount { get; set; }
}

public class SprintDetailDto : SprintListItemDto
{
    public List<SprintTaskDto> Tasks { get; set; } = [];
}

public class SprintTaskDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Priority Priority { get; set; }
    public bool IsCompleted { get; set; }
    public int? AssignedToUserId { get; set; }
    public string? AssignedToEmail { get; set; }
    public int SprintOrder { get; set; }
    public int BoardColumnId { get; set; }
    public string? BoardColumnTitle { get; set; }
    public int SubtaskDoneCount { get; set; }
    public int SubtaskTotal { get; set; }
}

public class BoardKapsamDto
{
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int BoardId { get; set; }
    public string BoardName { get; set; } = string.Empty;
    public List<SprintTaskDto> BacklogTasks { get; set; } = [];
    public List<SprintDetailDto> Sprints { get; set; } = [];
}

public class CreateSprintRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Goal { get; set; }
    public DateTime PlannedStartDate { get; set; }
    public DateTime PlannedEndDate { get; set; }
}

public class UpdateSprintRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Goal { get; set; }
    public DateTime PlannedStartDate { get; set; }
    public DateTime PlannedEndDate { get; set; }
}

public class MoveTaskToSprintRequest
{
    public int? TargetIndex { get; set; }
}

public class ReorderSprintTasksRequest
{
    public List<int> TaskIds { get; set; } = [];
}

public class ReorderSprintsRequest
{
    public List<int> SprintIds { get; set; } = [];
}

/// <summary>incompleteDestination: "backlog" veya "sprint".</summary>
public class CompleteSprintRequest
{
    public string IncompleteDestination { get; set; } = "backlog";
    public int? TargetSprintId { get; set; }
}

public class CancelSprintRequest
{
    public string TaskDestination { get; set; } = "backlog";
    public int? TargetSprintId { get; set; }
}
