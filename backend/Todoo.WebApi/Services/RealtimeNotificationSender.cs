using Microsoft.AspNetCore.SignalR;
using Todoo.Business.Abstract;
using Todoo.Business.Models.Notifications;
using Todoo.WebApi.Hubs;

namespace Todoo.WebApi.Services;

public class RealtimeNotificationSender : IRealtimeNotificationSender
{
    private readonly IHubContext<NotificationHub> _hubContext;

    public RealtimeNotificationSender(IHubContext<NotificationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task SendToUserAsync(int userId, NotificationItemDto notification, int unreadCount)
    {
        return _hubContext.Clients
            .Group(NotificationHub.UserGroup(userId))
            .SendAsync("NotificationReceived", new { notification, unreadCount });
    }
}
