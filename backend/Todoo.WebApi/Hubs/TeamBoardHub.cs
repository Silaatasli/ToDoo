using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Todoo.Business.Abstract;

namespace Todoo.WebApi.Hubs;

[Authorize]
public class TeamBoardHub : Hub
{
    private readonly ITeamService _teamService;

    public TeamBoardHub(ITeamService teamService)
    {
        _teamService = teamService;
    }

    public static string TeamGroup(int teamId) => $"team-{teamId}";

    public async Task JoinTeam(int teamId)
    {
        if (!TryGetUserId(out var userId))
        {
            throw new HubException("Gecerli bir kullanici bilgisi bulunamadi.");
        }

        if (!await _teamService.IsTeamMemberAsync(teamId, userId))
        {
            throw new HubException("Bu takimin uyesi degilsiniz.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, TeamGroup(teamId));
    }

    public Task LeaveTeam(int teamId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, TeamGroup(teamId));
    }

    public async Task NotifyTaskDragStart(int teamId, int taskId, int sourceColumnId)
    {
        if (!TryGetUserId(out var userId))
        {
            return;
        }

        if (!await _teamService.IsTeamMemberAsync(teamId, userId))
        {
            return;
        }

        await Clients.OthersInGroup(TeamGroup(teamId)).SendAsync(
            "TaskDragStarted",
            new { teamId, taskId, sourceColumnId, userId });
    }

    public async Task NotifyTaskDragMove(int teamId, int taskId, int hoverColumnId)
    {
        if (!TryGetUserId(out var userId))
        {
            return;
        }

        if (!await _teamService.IsTeamMemberAsync(teamId, userId))
        {
            return;
        }

        await Clients.OthersInGroup(TeamGroup(teamId)).SendAsync(
            "TaskDragMoved",
            new { teamId, taskId, hoverColumnId, userId });
    }

    public async Task NotifyTaskDragEnd(int teamId, int taskId)
    {
        if (!TryGetUserId(out var userId))
        {
            return;
        }

        if (!await _teamService.IsTeamMemberAsync(teamId, userId))
        {
            return;
        }

        await Clients.OthersInGroup(TeamGroup(teamId)).SendAsync(
            "TaskDragEnded",
            new { teamId, taskId, userId });
    }

    private bool TryGetUserId(out int userId)
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out userId);
    }
}
