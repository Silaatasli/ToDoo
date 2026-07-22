using Todoo.Entities.Enums;

namespace Todoo.Business.Models.Teams;

public class TeamAnnouncementDto
{
    public int Id { get; set; } // duyuru id

    public int TeamId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public AnnouncementStatus Status { get; set; }

    public DateTime? ScheduledPublishAt { get; set; }

    public DateTime? PublishedAt { get; set; }

    public int AuthorUserId { get; set; }

    public string AuthorDisplayName { get; set; } = string.Empty;

    public string AuthorEmail { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; }
}
