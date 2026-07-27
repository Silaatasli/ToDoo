namespace Todoo.Business.Models.Reports;

public class SlaPerformanceDto
{
    public int TeamId { get; set; }

    public int UserId { get; set; }

    public string? DisplayName { get; set; }

    /// <summary>Onem agirlikli SLA uyum yuzdesi (Met/Breached).</summary>
    public int? CompliancePercent { get; set; }

    public int MetCount { get; set; }

    public int BreachedCount { get; set; }

    public int OnTrackCount { get; set; }

    public List<SlaTaskItemDto> RecentMet { get; set; } = [];

    public List<SlaTaskItemDto> RecentBreached { get; set; } = [];
}

public class SlaTaskItemDto
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTime? DueDate { get; set; }

    public DateTime? CompletedAt { get; set; }

    public int Priority { get; set; }
}

public class TeamSlaMembersDto
{
    public int TeamId { get; set; }

    public List<SlaPerformanceDto> Members { get; set; } = [];
}
