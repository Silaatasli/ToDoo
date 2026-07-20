using Todoo.Business.Models.Notifications;

namespace Todoo.Business.Abstract;

/// <summary>
/// Anlik bildirim iletimi (SignalR). WebApi katmaninda implement edilir.
/// </summary>
public interface IRealtimeNotificationSender
{
    Task SendToUserAsync(int userId, NotificationItemDto notification, int unreadCount);
}
