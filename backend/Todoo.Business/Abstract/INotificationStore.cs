using Todoo.Business.Models.Notifications;

namespace Todoo.Business.Abstract;

public interface INotificationStore
{
    Task AddAsync(NotificationMessage message, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationItemDto>> ListAsync(int userId, int take = 30);

    Task<int> GetUnreadCountAsync(int userId);

    Task<bool> MarkReadAsync(int userId, string notificationId);

    Task MarkAllReadAsync(int userId);
}
