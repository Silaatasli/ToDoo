using Todoo.Business.Models;
using Todoo.Business.Models.Teams;

namespace Todoo.Business.Abstract;

public interface ITeamAnnouncementService
{
    Task<ServiceResult<IEnumerable<TeamAnnouncementDto>>> ListAsync(int teamId, int userId);

    Task<ServiceResult<TeamAnnouncementDto>> CreateAsync(
        int teamId,
        string title,
        string body,
        string publishMode,
        DateTime? scheduledPublishAt,
        int userId);

    Task<ServiceResult<TeamAnnouncementDto>> PublishAsync(int teamId, int announcementId, int userId);

    Task<ServiceResult> DeleteAsync(int teamId, int announcementId, int userId);

    Task<ServiceResult> SetPublishPermissionAsync(int teamId, int memberUserId, bool canPublish, int actorUserId);

    Task<int> PublishDueScheduledAsync(CancellationToken cancellationToken = default);

    Task<DateTime?> GetNextScheduledPublishAtUtcAsync(CancellationToken cancellationToken = default);
}
