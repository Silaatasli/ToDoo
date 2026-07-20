using Microsoft.Extensions.Logging;
using Todoo.Business.Abstract;
using Todoo.Business.Models.Notifications;

namespace Todoo.Business.Concrete;

/// <summary>
/// Bildirim yayini icin ortak yardimci. RabbitMQ hatasi ana islemi bozmaz.
/// Announcement / Mention icin de ayni API kullanilir (ileride).
/// </summary>
public class NotificationDispatchService
{
    private readonly INotificationPublisher _publisher;
    private readonly ILogger<NotificationDispatchService> _logger;

    public NotificationDispatchService(
        INotificationPublisher publisher,
        ILogger<NotificationDispatchService> logger)
    {
        _publisher = publisher;
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

    /// <summary>Takim duyurusu. includeActor=true ile yayinciya da bildirim gider (zamanlanmis yayin).</summary>
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

        var tasks = memberUserIds
            .Where(id => includeActor || id != actorUserId)
            .Distinct()
            .Select(userId => PublishSafeAsync(new NotificationMessage
            {
                Type = NotificationTypes.Announcement,
                TargetUserId = userId,
                ActorUserId = actorUserId,
                Title = announcementTitle,
                Body = Truncate(messageBody, 220),
                TeamId = teamId,
                AnnouncementId = announcementId
            }));

        return Task.WhenAll(tasks);
    }

    /// <summary>@mention (ileride mention parse edildikten sonra cagrilacak).</summary>
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
            await _publisher.PublishAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Bildirim kuyruga yazilamadi. Type={Type}, TargetUserId={TargetUserId}",
                message.Type,
                message.TargetUserId);
        }
    }

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
