namespace Todoo.Business.Models.Reports;

public class SlaActiveSprintContextDto
{
    public int SprintId { get; set; }

    public string SprintName { get; set; } = string.Empty;

    public int BoardId { get; set; }

    public string BoardName { get; set; } = string.Empty;

    /// <summary>Sprint planlanan bitis — SLA formülüne girmez, sadece baglam.</summary>
    public DateTime PlannedEndDate { get; set; }
}

public class SlaPerformanceDto
{
    public int TeamId { get; set; }

    public int UserId { get; set; }

    public string? DisplayName { get; set; }

    /// <summary>Onem agirlikli SLA uyum yuzdesi (Met/Breached). Gorev DueDate ile hesaplanir.</summary>
    public int? CompliancePercent { get; set; }

    public int MetCount { get; set; }

    public int BreachedCount { get; set; }

    public int OnTrackCount { get; set; }

    /// <summary>Bu takimda SLA kapsaminda olan aktif sprintler (bilgi).</summary>
    public List<SlaActiveSprintContextDto> ActiveSprints { get; set; } = [];

    public bool HasActiveSprint => ActiveSprints.Count > 0;

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

    public int? SprintId { get; set; }

    public string? SprintName { get; set; }
}

public class TeamSlaMembersDto
{
    public int TeamId { get; set; }

    public List<SlaActiveSprintContextDto> ActiveSprints { get; set; } = [];

    public bool HasActiveSprint => ActiveSprints.Count > 0;

    public List<SlaPerformanceDto> Members { get; set; } = [];
}
