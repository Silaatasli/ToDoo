namespace Todoo.Business.Models.Notifications;

/// <summary>
/// RabbitMQ uzerinden tasinan bildirim mesaji. Consumer Redis'e yazar ve SignalR ile iletir.
/// </summary>
public class NotificationMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Type { get; set; } = string.Empty;

    public int TargetUserId { get; set; }

    public int? ActorUserId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public int? TeamId { get; set; }

    public int? BoardId { get; set; }

    public int? TaskId { get; set; }

    public int? AnnouncementId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
