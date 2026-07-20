using System.Text.Json.Serialization;
using Todoo.Entities.Enums;

namespace Todoo.Entities.Entities;

public class TeamAnnouncement
{
    public int Id { get; set; }

    public int TeamId { get; set; }

    public int AuthorUserId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public AnnouncementStatus Status { get; set; } = AnnouncementStatus.Draft;

    public DateTime? ScheduledPublishAt { get; set; }

    public DateTime? PublishedAt { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public Team Team { get; set; } = null!;

    [JsonIgnore]
    public User Author { get; set; } = null!;
}
