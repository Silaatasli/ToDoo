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
            // IUserIdProvider ile eslesen online baglantilar
            await _hubContext.Clients
                .User(userKey)
                .SendAsync("NotificationReceived", payload);

            // Ayni kullanicinin group aboneligi (eski baglantilar / yedek)
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
}
