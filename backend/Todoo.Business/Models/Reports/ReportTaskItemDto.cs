namespace Todoo.Business.Models.Reports;

public class ReportTaskItemDto
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTime? DueDate { get; set; }

    public string? BoardColumnTitle { get; set; }
}
