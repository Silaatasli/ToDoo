namespace Todoo.Business.Models.Sprints;

public class SprintAuditEntryDto
{
    public string Id { get; set; } = string.Empty;

    public int TeamId { get; set; }

    public int BoardId { get; set; }

    public int SprintId { get; set; }

    public string SprintName { get; set; } = string.Empty;

    public int? TaskId { get; set; }

    public int UserId { get; set; }

    public string? UserEmail { get; set; }

    public string ActionType { get; set; } = string.Empty;

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public DateTime CreatedDate { get; set; }

    public string Source { get; set; } = "opensearch";
}

public class SprintAuditWriteRequest
{
    public int TeamId { get; set; }

    public int BoardId { get; set; }

    public int SprintId { get; set; }

    public string SprintName { get; set; } = string.Empty;

    public int? TaskId { get; set; }

    public int UserId { get; set; }

    public string? UserEmail { get; set; }

    public string ActionType { get; set; } = string.Empty;

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
