using Todoo.Business.Models.Notifications;

namespace Todoo.Business.Abstract;

/// <summary>
/// Anlik bildirim iletimi (SignalR). WebApi katmaninda implement edilir.
/// </summary>
public interface IRealtimeNotificationSender
{
    /// <summary>Kisiye ozel bildirim (atama, mention, vb.).</summary>
    Task SendToUserAsync(int userId, NotificationItemDto notification, int unreadCount);

    /// <summary>Takim grubuna tek seferlik broadcast (duyuru vb.).</summary>
    Task SendToTeamAsync(int teamId, NotificationItemDto notification, int? excludeUserId = null);
}
