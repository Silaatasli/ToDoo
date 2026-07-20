using System.ComponentModel.DataAnnotations;

namespace Todoo.WebApi.Models.Teams;

public class CreateTeamAnnouncementRequestDto
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(4000)]
    public string Body { get; set; } = string.Empty;

    /// <summary>Draft | Now | Schedule</summary>
    [Required]
    [MaxLength(20)]
    public string PublishMode { get; set; } = "Now";

    public DateTime? ScheduledPublishAt { get; set; }
}
