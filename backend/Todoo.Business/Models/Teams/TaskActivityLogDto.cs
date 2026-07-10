using Todoo.Entities.Enums;

namespace Todoo.Business.Models.Teams;

public class TaskActivityLogDto
{
    public int Id { get; set; }

    public int TaskId { get; set; }

    public int UserId { get; set; }

    public string UserEmail { get; set; } = string.Empty;

    public TaskActivityAction ActionType { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public DateTime CreatedDate { get; set; }
}
