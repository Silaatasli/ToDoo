using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Todoo.Business.Abstract;
using Todoo.Business.Models.Notifications;
using Todoo.WebApi.Hubs;

namespace Todoo.WebApi.Services;

public class RealtimeNotificationSender : IRealtimeNotificationSender
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<RealtimeNotificationSender> _logger;

    public RealtimeNotificationSender(
        IHubContext<NotificationHub> hubContext,
        ILogger<RealtimeNotificationSender> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task SendToUserAsync(int userId, NotificationItemDto notification, int unreadCount)
    {
        var payload = new { notification, unreadCount };
        var userKey = userId.ToString();

        try
        {
            await _hubContext.Clients
                .User(userKey)
                .SendAsync("NotificationReceived", payload);

            await _hubContext.Clients
                .Group(NotificationHub.UserGroup(userId))
                .SendAsync("NotificationReceived", payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalR bildirim gonderilemedi. UserId={UserId}", userId);
            throw;
        }
    }

    public async Task SendToTeamAsync(int teamId, NotificationItemDto notification, int? excludeUserId = null)
    {
        var payload = new
        {
            notification,
            teamId,
            excludeUserId
        };

        try
        {
            await _hubContext.Clients
                .Group(NotificationHub.TeamGroup(teamId))
                .SendAsync("TeamNotificationReceived", payload);

            _logger.LogDebug(
                "Takim bildirimi broadcast edildi. TeamId={TeamId}, Type={Type}",
                teamId,
                notification.Type);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalR takim bildirimi gonderilemedi. TeamId={TeamId}", teamId);
            throw;
        }
    }
}
