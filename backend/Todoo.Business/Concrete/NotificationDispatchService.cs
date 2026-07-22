using Microsoft.Extensions.Logging;
using Todoo.Business.Abstract;
using Todoo.Business.Models.Notifications;

namespace Todoo.Business.Concrete;

/// <summary>
/// Bildirim yayini. Once Redis + SignalR ile dogrudan iletir;
/// RabbitMQ varsa ek olarak kuyruga da yazar (consumer cift kayit yapmamasi icin DirectDelivered bayragi).
/// </summary>
public class NotificationDispatchService
{
    private readonly INotificationPublisher _publisher;
    private readonly INotificationStore _store;
    private readonly IRealtimeNotificationSender _realtime;
    private readonly ILogger<NotificationDispatchService> _logger;

    public NotificationDispatchService(
        INotificationPublisher publisher,
        INotificationStore store,
        IRealtimeNotificationSender realtime,
        ILogger<NotificationDispatchService> logger)
    {
        _publisher = publisher;
        _store = store;
        _realtime = realtime;
        _logger = logger;
    }

    public Task NotifyTaskAssignedAsync(
        int targetUserId,
        int actorUserId,
        int teamId,
        int? boardId,
        int taskId,
        string taskTitle)
    {
        if (targetUserId == actorUserId)
        {
            return Task.CompletedTask;
        }

        return PublishSafeAsync(new NotificationMessage
        {
            Type = NotificationTypes.TaskAssigned,
            TargetUserId = targetUserId,
            ActorUserId = actorUserId,
            Title = "Yeni görev ataması",
            Body = $"Sana \"{Truncate(taskTitle, 80)}\" görevi atandı.",
            TeamId = teamId,
            BoardId = boardId,
            TaskId = taskId
        });
    }

    public Task NotifyCommentReplyAsync(
        int targetUserId,
        int actorUserId,
        int teamId,
        int? boardId,
        int taskId,
        string taskTitle,
        string replyPreview)
    {
        if (targetUserId == actorUserId)
        {
            return Task.CompletedTask;
        }

        return PublishSafeAsync(new NotificationMessage
        {
            Type = NotificationTypes.CommentReply,
            TargetUserId = targetUserId,
            ActorUserId = actorUserId,
            Title = "Yorumuna cevap geldi",
            Body = $"\"{Truncate(taskTitle, 60)}\" görevinde: {Truncate(replyPreview, 100)}",
            TeamId = teamId,
            BoardId = boardId,
            TaskId = taskId
        });
    }

    public Task NotifyTeamMemberAddedAsync(
        int targetUserId,
        int actorUserId,
        int teamId,
        string teamName,
        string actorDisplayName)
    {
        if (targetUserId == actorUserId)
        {
            return Task.CompletedTask;
        }

        var actor = string.IsNullOrWhiteSpace(actorDisplayName) ? "Bir kullanıcı" : actorDisplayName.Trim();
        var team = string.IsNullOrWhiteSpace(teamName) ? "bir takım" : $"\"{teamName.Trim()}\"";

        return PublishSafeAsync(new NotificationMessage
        {
            Type = NotificationTypes.TeamMemberAdded,
            TargetUserId = targetUserId,
            ActorUserId = actorUserId,
            Title = "Takıma eklendin",
            Body = $"{actor} seni {team} takımına ekledi.",
            TeamId = teamId
        });
    }

    /// <summary>Takim duyurusu. Yayinciya bildirim gitmez (includeActor yalnizca ozel durumlar icin).</summary>
    public Task NotifyAnnouncementAsync(
        IEnumerable<int> memberUserIds,
        int actorUserId,
        int teamId,
        int announcementId,
        string title,
        string body,
        string teamName,
        string actorDisplayName,
        bool includeActor = false)
    {
        var actor = string.IsNullOrWhiteSpace(actorDisplayName) ? "Bir kullanıcı" : actorDisplayName.Trim();
        var team = string.IsNullOrWhiteSpace(teamName) ? "bir takım" : teamName.Trim();
        var announcementTitle = Truncate(
            string.IsNullOrWhiteSpace(title) ? "Takım duyurusu" : title.Trim(),
            120);
        var preview = Truncate(body, 120);
        var meta = $"{actor} · {team}";
        var messageBody = string.IsNullOrWhiteSpace(preview) ? meta : $"{meta}: {preview}";

        var recipients = memberUserIds
            .Where(id => id > 0)
            .Where(id => includeActor || id != actorUserId)
            .Distinct()
            .ToList();

        if (recipients.Count == 0)
        {
            _logger.LogWarning(
                "Duyuru bildirimi icin alici yok. TeamId={TeamId}, AnnouncementId={AnnouncementId}, ActorUserId={ActorUserId}",
                teamId,
                announcementId,
                actorUserId);
            return Task.CompletedTask;
        }

        _logger.LogInformation(
            "Duyuru bildirimi gonderiliyor. TeamId={TeamId}, AnnouncementId={AnnouncementId}, Recipients={Count}",
            teamId,
            announcementId,
            recipients.Count);

        var tasks = recipients.Select(userId => PublishSafeAsync(new NotificationMessage
        {
            Type = NotificationTypes.Announcement,
            TargetUserId = userId,
            ActorUserId = actorUserId,
            Title = announcementTitle,
            Body = Truncate(messageBody, 220),
            TeamId = teamId,
            AnnouncementId = announcementId,
            DirectDelivered = true
        }));

        return Task.WhenAll(tasks);
    }

    public Task NotifyMentionAsync(
        int targetUserId,
        int actorUserId,
        int teamId,
        int? boardId,
        int taskId,
        string taskTitle,
        string preview)
    {
        if (targetUserId == actorUserId)
        {
            return Task.CompletedTask;
        }

        return PublishSafeAsync(new NotificationMessage
        {
            Type = NotificationTypes.Mention,
            TargetUserId = targetUserId,
            ActorUserId = actorUserId,
            Title = "Bir yorumda senden bahsedildi",
            Body = $"\"{Truncate(taskTitle, 60)}\": {Truncate(preview, 100)}",
            TeamId = teamId,
            BoardId = boardId,
            TaskId = taskId
        });
    }

    private async Task PublishSafeAsync(NotificationMessage message)
    {
        try
        {
            // Birincil yol: Redis + SignalR (RabbitMQ ayakta olmasa da bildirim duser).
            await _store.AddAsync(message);
            var unread = await _store.GetUnreadCountAsync(message.TargetUserId);
            await _realtime.SendToUserAsync(message.TargetUserId, ToDto(message), unread);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Bildirim dogrudan iletilemedi. Type={Type}, TargetUserId={TargetUserId}",
                message.Type,
                message.TargetUserId);

            try
            {
                message.DirectDelivered = false;
                await _publisher.PublishAsync(message);
            }
            catch (Exception publishEx)
            {
                _logger.LogError(
                    publishEx,
                    "Bildirim kuyruga da yazilamadi. Type={Type}, TargetUserId={TargetUserId}",
                    message.Type,
                    message.TargetUserId);
            }

            return;
        }

        // Istege bagli kuyruk (baska consumer'lar icin); DirectDelivered=true ile cift kayit engellenir.
        try
        {
            message.DirectDelivered = true;
            await _publisher.PublishAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Bildirim kuyruga yazilamadi (dogrudan iletim basarili). Type={Type}, TargetUserId={TargetUserId}",
                message.Type,
                message.TargetUserId);
        }
    }

    private static NotificationItemDto ToDto(NotificationMessage message) => new()
    {
        Id = message.Id,
        Type = message.Type,
        Title = message.Title,
        Body = message.Body,
        TeamId = message.TeamId,
        BoardId = message.BoardId,
        TaskId = message.TaskId,
        AnnouncementId = message.AnnouncementId,
        IsRead = false,
        CreatedAtUtc = message.CreatedAtUtc
    };

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : $"{trimmed[..max]}...";
    }
}
