namespace Todoo.Business.Models.Notifications;

public class NotificationItemDto
{
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public int? TeamId { get; set; }

    public int? BoardId { get; set; }

    public int? TaskId { get; set; }

    public int? AnnouncementId { get; set; }

    public int? SprintId { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
