using Todoo.Business.Abstract;
using Todoo.Business.Helpers;
using Todoo.Business.Models;
using Todoo.Business.Models.Reports;
using Todoo.DataAccess.UnitOfWork;
using Todoo.Entities.Entities;

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

        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        var dto = BuildPerformanceDto(
            teamId,
            userId,
            user is null ? string.Empty : UserDisplayNameHelper.Format(user),
            await GetAssignableTasksAsync(teamId, userId));

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

        var members = (await _unitOfWork.TeamMembers.GetAllAsync())
            .Where(item => item.TeamId == teamId)
            .ToList();
        var userIds = members.Select(item => item.UserId).Distinct().ToList();
        var users = (await _unitOfWork.Users.GetAllAsync())
            .Where(user => userIds.Contains(user.Id))
            .ToDictionary(user => user.Id);

        var teamTasks = await GetTeamParentTasksAsync(teamId);
        var memberDtos = new List<SlaPerformanceDto>();

        foreach (var member in members.OrderBy(item => item.UserId))
        {
            if (!users.TryGetValue(member.UserId, out var user))
            {
                continue;
            }

            var assigned = teamTasks.Where(task => task.AssignedToUserId == member.UserId).ToList();
            memberDtos.Add(BuildPerformanceDto(
                teamId,
                member.UserId,
                UserDisplayNameHelper.Format(user),
                assigned));
        }

        return ServiceResult<TeamSlaMembersDto>.Ok(new TeamSlaMembersDto
        {
            TeamId = teamId,
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

    private async Task<List<TaskItem>> GetAssignableTasksAsync(int teamId, int userId)
    {
        return (await GetTeamParentTasksAsync(teamId))
            .Where(task => task.AssignedToUserId == userId)
            .ToList();
    }

    private async Task<List<TaskItem>> GetTeamParentTasksAsync(int teamId)
    {
        return (await _unitOfWork.TaskItems.GetAllAsync())
            .Where(task =>
                task.TeamId == teamId
                && !task.ParentTaskId.HasValue
                && !task.DeletedAt.HasValue)
            .ToList();
    }

    private static SlaPerformanceDto BuildPerformanceDto(
        int teamId,
        int userId,
        string displayName,
        List<TaskItem> tasks)
    {
        var now = DateTime.UtcNow;
        var (metWeight, totalResolvedWeight, metCount, breachedCount, onTrackCount) =
            SlaCalculator.Summarize(tasks, now);

        var breachedTasks = tasks
            .Where(task => SlaCalculator.GetStatus(task, now) == SlaStatus.Breached)
            .OrderByDescending(task => task.DueDate)
            .Take(10)
            .Select(MapTaskItem)
            .ToList();
        var metTasks = tasks
            .Where(task => SlaCalculator.GetStatus(task, now) == SlaStatus.Met)
            .OrderByDescending(task => task.CompletedAt ?? task.DueDate)
            .Take(10)
            .Select(MapTaskItem)
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
            RecentMet = metTasks,
            RecentBreached = breachedTasks
        };
    }

    private static SlaTaskItemDto MapTaskItem(TaskItem task) => new()
    {
        Id = task.Id,
        Title = task.Title,
        DueDate = task.DueDate,
        CompletedAt = task.CompletedAt,
        Priority = (int)task.Priority
    };
}
