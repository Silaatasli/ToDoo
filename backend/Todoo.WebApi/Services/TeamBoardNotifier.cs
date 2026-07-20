using Microsoft.AspNetCore.SignalR;
using Todoo.Business.Abstract;
using Todoo.WebApi.Hubs;

namespace Todoo.WebApi.Services;

public class TeamBoardNotifier : ITeamBoardNotifier
{
    private readonly IHubContext<TeamBoardHub> _hubContext;

    public TeamBoardNotifier(IHubContext<TeamBoardHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyBoardChangedAsync(
        int teamId,
        string changeType,
        int? actorUserId = null,
        int? taskId = null,
        int? boardId = null,
        int? announcementId = null)
    {
        return _hubContext.Clients
            .Group(TeamBoardHub.TeamGroup(teamId))
            .SendAsync("BoardChanged", new { teamId, boardId, changeType, actorUserId, taskId, announcementId });
    }
}
