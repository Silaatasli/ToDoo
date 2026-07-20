using Todoo.Business.Models.Notifications;

namespace Todoo.Business.Abstract;

public interface INotificationPublisher
{
    Task PublishAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}
