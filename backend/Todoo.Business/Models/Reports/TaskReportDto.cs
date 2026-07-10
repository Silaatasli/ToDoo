namespace Todoo.Business.Models.Reports;

public class TaskReportDto
{
    public int CompletedTaskCount { get; set; }

    public int ActiveTaskCount { get; set; }

    public int OverdueTaskCount { get; set; }

    public int UpcomingTaskCount { get; set; }

    public int LowPriorityCount { get; set; }

    public int MediumPriorityCount { get; set; }

    public int HighPriorityCount { get; set; }

    public int CriticalPriorityCount { get; set; }

    public string? MostUsedCategoryName { get; set; }

    public List<ReportTaskItemDto> OverdueTasks { get; set; } = [];

    public List<ReportTaskItemDto> UpcomingTasks { get; set; } = [];
}
