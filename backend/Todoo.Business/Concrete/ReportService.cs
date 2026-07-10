using Todoo.Business.Abstract;
using Todoo.Business.Models;
using Todoo.Business.Models.Reports;
using Todoo.DataAccess.UnitOfWork;
using Todoo.Entities.Enums;

namespace Todoo.Business.Concrete;

public class ReportService : IReportService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITeamService _teamService;

    public ReportService(IUnitOfWork unitOfWork, ITeamService teamService)
    {
        _unitOfWork = unitOfWork;
        _teamService = teamService;
    }

    public async Task<ServiceResult<TaskReportDto>> GetTaskReportByTeamIdAsync(int teamId, int userId)
    {
        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        if (team is null)
        {
            return ServiceResult<TaskReportDto>.Fail("Takim bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (!await _teamService.IsTeamMemberAsync(teamId, userId))
        {
            return ServiceResult<TaskReportDto>.Fail("Bu takimin uyesi degilsiniz.", ServiceErrorKind.Forbidden);
        }

        var teamTasks = (await _unitOfWork.TaskItems.GetAllAsync())
            .Where(task => task.TeamId == teamId)
            .ToList();
        var columnMap = (await _unitOfWork.TeamBoardColumns.GetAllAsync())
            .Where(column => column.TeamId == teamId)
            .ToDictionary(column => column.Id, column => column.Title);

        var mostUsedCategoryId = teamTasks
            .Where(task => task.CategoryId.HasValue)
            .GroupBy(task => task.CategoryId)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();

        string? mostUsedCategoryName = null;
        if (mostUsedCategoryId.HasValue)
        {
            mostUsedCategoryName = (await _unitOfWork.Categories.GetByIdAsync(mostUsedCategoryId.Value))?.Name;
        }

        var todayUtc = DateTime.UtcNow.Date;
        var tomorrowUtc = todayUtc.AddDays(1);
        var overdueTasks = teamTasks
            .Where(task =>
                !task.IsCompleted &&
                task.DueDate.HasValue &&
                task.DueDate.Value.Date < todayUtc)
            .OrderBy(task => task.DueDate)
            .Select(task => new ReportTaskItemDto
            {
                Id = task.Id,
                Title = task.Title,
                DueDate = task.DueDate,
                BoardColumnTitle = columnMap.GetValueOrDefault(task.BoardColumnId)
            })
            .ToList();
        var upcomingTasks = teamTasks
            .Where(task =>
                !task.IsCompleted &&
                task.DueDate.HasValue &&
                task.DueDate.Value.Date >= todayUtc &&
                task.DueDate.Value.Date <= tomorrowUtc)
            .OrderBy(task => task.DueDate)
            .Select(task => new ReportTaskItemDto
            {
                Id = task.Id,
                Title = task.Title,
                DueDate = task.DueDate,
                BoardColumnTitle = columnMap.GetValueOrDefault(task.BoardColumnId)
            })
            .ToList();

        return ServiceResult<TaskReportDto>.Ok(new TaskReportDto
        {
            CompletedTaskCount = teamTasks.Count(task => task.IsCompleted),
            ActiveTaskCount = teamTasks.Count(task => !task.IsCompleted),
            OverdueTaskCount = overdueTasks.Count,
            UpcomingTaskCount = upcomingTasks.Count,
            LowPriorityCount = teamTasks.Count(task => task.Priority == Priority.Low),
            MediumPriorityCount = teamTasks.Count(task => task.Priority == Priority.Medium),
            HighPriorityCount = teamTasks.Count(task => task.Priority == Priority.High),
            CriticalPriorityCount = teamTasks.Count(task => task.Priority == Priority.Critical),
            MostUsedCategoryName = mostUsedCategoryName,
            OverdueTasks = overdueTasks,
            UpcomingTasks = upcomingTasks
        });
    }
}
