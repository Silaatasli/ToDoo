using Todoo.Business.Abstract;
using Todoo.Business.Helpers;
using Todoo.Business.Models;
using Todoo.Business.Models.Reports;
using Todoo.DataAccess.UnitOfWork;
using Todoo.Entities.Entities;
using Todoo.Entities.Enums;

namespace Todoo.Business.Concrete;

public class SlaPerformanceService : ISlaPerformanceService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITeamService _teamService;

    public SlaPerformanceService(IUnitOfWork unitOfWork, ITeamService teamService)
    {
        _unitOfWork = unitOfWork;
        _teamService = teamService;
    }

    public async Task<ServiceResult<SlaPerformanceDto>> GetMyPerformanceAsync(int teamId, int userId)
    {
        var access = await EnsureTeamMemberAsync(teamId, userId);
        if (!access.Success)
        {
            return ServiceResult<SlaPerformanceDto>.Fail(access.ErrorMessage!, access.ErrorKind ?? ServiceErrorKind.Validation);
        }

        var activeSprints = await GetActiveSprintContextsAsync(teamId);
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        var tasks = await GetActiveSprintAssignedTasksAsync(teamId, userId, activeSprints);
        var dto = BuildPerformanceDto(
            teamId,
            userId,
            user is null ? string.Empty : UserDisplayNameHelper.Format(user),
            tasks,
            activeSprints);

        return ServiceResult<SlaPerformanceDto>.Ok(dto);
    }

    public async Task<ServiceResult<TeamSlaMembersDto>> GetTeamMembersPerformanceAsync(int teamId, int requesterUserId)
    {
        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        if (team is null)
        {
            return ServiceResult<TeamSlaMembersDto>.Fail("Takim bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (!await _teamService.IsTeamMemberAsync(teamId, requesterUserId))
        {
            return ServiceResult<TeamSlaMembersDto>.Fail("Bu takimin uyesi degilsiniz.", ServiceErrorKind.Forbidden);
        }

        if (team.LeaderUserId != requesterUserId)
        {
            return ServiceResult<TeamSlaMembersDto>.Fail("Uye SLA ozetini yalnizca takim lideri gorebilir.", ServiceErrorKind.Forbidden);
        }

        var activeSprints = await GetActiveSprintContextsAsync(teamId);
        var members = (await _unitOfWork.TeamMembers.GetAllAsync())
            .Where(item => item.TeamId == teamId)
            .ToList();
        var userIds = members.Select(item => item.UserId).Distinct().ToList();
        var users = (await _unitOfWork.Users.GetAllAsync())
            .Where(user => userIds.Contains(user.Id))
            .ToDictionary(user => user.Id);

        var scopedTasks = await GetActiveSprintParentTasksAsync(teamId, activeSprints);
        var memberDtos = new List<SlaPerformanceDto>();

        foreach (var member in members.OrderBy(item => item.UserId))
        {
            if (!users.TryGetValue(member.UserId, out var user))
            {
                continue;
            }

            var assigned = scopedTasks.Where(task => task.AssignedToUserId == member.UserId).ToList();
            memberDtos.Add(BuildPerformanceDto(
                teamId,
                member.UserId,
                UserDisplayNameHelper.Format(user),
                assigned,
                activeSprints));
        }

        return ServiceResult<TeamSlaMembersDto>.Ok(new TeamSlaMembersDto
        {
            TeamId = teamId,
            ActiveSprints = activeSprints,
            Members = memberDtos
        });
    }

    private async Task<ServiceResult> EnsureTeamMemberAsync(int teamId, int userId)
    {
        var team = await _unitOfWork.Teams.GetByIdAsync(teamId);
        if (team is null)
        {
            return ServiceResult.Fail("Takim bulunamadi.", ServiceErrorKind.NotFound);
        }

        if (!await _teamService.IsTeamMemberAsync(teamId, userId))
        {
            return ServiceResult.Fail("Bu takimin uyesi degilsiniz.", ServiceErrorKind.Forbidden);
        }

        return ServiceResult.Ok();
    }

    private async Task<List<SlaActiveSprintContextDto>> GetActiveSprintContextsAsync(int teamId)
    {
        var boards = (await _unitOfWork.Boards.GetAllAsync())
            .Where(board => board.TeamId == teamId)
            .ToDictionary(board => board.Id, board => board.Name);

        return (await _unitOfWork.Sprints.GetAllAsync())
            .Where(sprint => sprint.TeamId == teamId && sprint.Status == SprintStatus.Active)
            .OrderBy(sprint => sprint.BoardId)
            .ThenBy(sprint => sprint.Id)
            .Select(sprint => new SlaActiveSprintContextDto
            {
                SprintId = sprint.Id,
                SprintName = sprint.Name,
                BoardId = sprint.BoardId,
                BoardName = boards.GetValueOrDefault(sprint.BoardId) ?? $"Pano #{sprint.BoardId}",
                PlannedEndDate = sprint.PlannedEndDate
            })
            .ToList();
    }

    private async Task<List<TaskItem>> GetActiveSprintAssignedTasksAsync(
        int teamId,
        int userId,
        IReadOnlyList<SlaActiveSprintContextDto> activeSprints)
    {
        return (await GetActiveSprintParentTasksAsync(teamId, activeSprints))
            .Where(task => task.AssignedToUserId == userId)
            .ToList();
    }

    private async Task<List<TaskItem>> GetActiveSprintParentTasksAsync(
        int teamId,
        IReadOnlyList<SlaActiveSprintContextDto> activeSprints)
    {
        if (activeSprints.Count == 0)
        {
            return [];
        }

        var activeSprintIds = activeSprints.Select(sprint => sprint.SprintId).ToHashSet();
        return (await _unitOfWork.TaskItems.GetAllAsync())
            .Where(task =>
                task.TeamId == teamId
                && !task.ParentTaskId.HasValue
                && !task.DeletedAt.HasValue
                && task.SprintId.HasValue
                && activeSprintIds.Contains(task.SprintId.Value))
            .ToList();
    }

    private static SlaPerformanceDto BuildPerformanceDto(
        int teamId,
        int userId,
        string displayName,
        List<TaskItem> tasks,
        IReadOnlyList<SlaActiveSprintContextDto> activeSprints)
    {
        var now = DateTime.UtcNow;
        var (metWeight, totalResolvedWeight, metCount, breachedCount, onTrackCount) =
            SlaCalculator.Summarize(tasks, now);

        var sprintNameById = activeSprints.ToDictionary(sprint => sprint.SprintId, sprint => sprint.SprintName);

        var breachedTasks = tasks
            .Where(task => SlaCalculator.GetStatus(task, now) == SlaStatus.Breached)
            .OrderByDescending(task => task.DueDate)
            .Take(10)
            .Select(task => MapTaskItem(task, sprintNameById))
            .ToList();
        var metTasks = tasks
            .Where(task => SlaCalculator.GetStatus(task, now) == SlaStatus.Met)
            .OrderByDescending(task => task.CompletedAt ?? task.DueDate)
            .Take(10)
            .Select(task => MapTaskItem(task, sprintNameById))
            .ToList();

        return new SlaPerformanceDto
        {
            TeamId = teamId,
            UserId = userId,
            DisplayName = displayName,
            CompliancePercent = SlaCalculator.ComputeCompliancePercent(metWeight, totalResolvedWeight),
            MetCount = metCount,
            BreachedCount = breachedCount,
            OnTrackCount = onTrackCount,
            ActiveSprints = activeSprints.ToList(),
            RecentMet = metTasks,
            RecentBreached = breachedTasks
        };
    }

    private static SlaTaskItemDto MapTaskItem(TaskItem task, IReadOnlyDictionary<int, string> sprintNameById) => new()
    {
        Id = task.Id,
        Title = task.Title,
        DueDate = task.DueDate,
        CompletedAt = task.CompletedAt,
        Priority = (int)task.Priority,
        SprintId = task.SprintId,
        SprintName = task.SprintId.HasValue && sprintNameById.TryGetValue(task.SprintId.Value, out var name)
            ? name
            : null
    };
}
