namespace Todoo.Business.Abstract;

public interface ITeamBoardNotifier
{
    Task NotifyBoardChangedAsync(int teamId, string changeType, int? actorUserId = null, int? taskId = null, int? boardId = null);
}
