using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Todoo.Business.Abstract;
using Todoo.Business.Models.Notifications;
using Todoo.Business.Options;

namespace Todoo.Business.Concrete;

public class RedisNotificationStore : INotificationStore
{
    private const string ListKeyPrefix = "todoo:notif:user:";
    private const string UnreadKeySuffix = ":unread";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly NotificationOptions _options;

    public RedisNotificationStore(IConnectionMultiplexer redis, IOptions<NotificationOptions> options)
    {
        _redis = redis;
        _options = options.Value;
    }

    private IDatabase Db => _redis.GetDatabase();

    private TimeSpan Ttl => TimeSpan.FromDays(Math.Max(1, _options.RetentionDays));

    private int MaxPerUser => Math.Max(1, _options.MaxPerUser);

    public async Task AddAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        var item = new NotificationItemDto
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

        var listKey = ListKey(message.TargetUserId);
        var unreadKey = UnreadKey(message.TargetUserId);
        var payload = JsonSerializer.Serialize(item, JsonOptions);

        await Db.ListLeftPushAsync(listKey, payload);
        await Db.ListTrimAsync(listKey, 0, MaxPerUser - 1);
        await Db.KeyExpireAsync(listKey, Ttl);
        await Db.StringIncrementAsync(unreadKey);
        await Db.KeyExpireAsync(unreadKey, Ttl);
    }

    public async Task<IReadOnlyList<NotificationItemDto>> ListAsync(int userId, int take = 30)
    {
        take = Math.Clamp(take, 1, MaxPerUser);
        var values = await Db.ListRangeAsync(ListKey(userId), 0, take - 1);
        var items = new List<NotificationItemDto>(values.Length);

        foreach (var value in values)
        {
            if (value.IsNullOrEmpty)
            {
                continue;
            }

            var item = JsonSerializer.Deserialize<NotificationItemDto>((string)value!, JsonOptions);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        return items;
    }

    public async Task<int> GetUnreadCountAsync(int userId)
    {
        var value = await Db.StringGetAsync(UnreadKey(userId));
        if (value.IsNullOrEmpty || !int.TryParse((string)value!, out var count))
        {
            return 0;
        }

        return Math.Max(0, count);
    }

    public async Task<bool> MarkReadAsync(int userId, string notificationId)
    {
        var listKey = ListKey(userId);
        var values = await Db.ListRangeAsync(listKey);
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i].IsNullOrEmpty)
            {
                continue;
            }

            var item = JsonSerializer.Deserialize<NotificationItemDto>((string)values[i]!, JsonOptions);
            if (item is null || !string.Equals(item.Id, notificationId, StringComparison.Ordinal))
            {
                continue;
            }

            if (item.IsRead)
            {
                return true;
            }

            item.IsRead = true;
            await Db.ListSetByIndexAsync(listKey, i, JsonSerializer.Serialize(item, JsonOptions));
            await DecrementUnreadAsync(userId);
            return true;
        }

        return false;
    }

    public async Task MarkAllReadAsync(int userId)
    {
        var listKey = ListKey(userId);
        var values = await Db.ListRangeAsync(listKey);
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i].IsNullOrEmpty)
            {
                continue;
            }

            var item = JsonSerializer.Deserialize<NotificationItemDto>((string)values[i]!, JsonOptions);
            if (item is null || item.IsRead)
            {
                continue;
            }

            item.IsRead = true;
            await Db.ListSetByIndexAsync(listKey, i, JsonSerializer.Serialize(item, JsonOptions));
        }

        await Db.StringSetAsync(UnreadKey(userId), 0, Ttl);
    }

    private async Task DecrementUnreadAsync(int userId)
    {
        var unreadKey = UnreadKey(userId);
        var next = await Db.StringDecrementAsync(unreadKey);
        if (next < 0)
        {
            await Db.StringSetAsync(unreadKey, 0, Ttl);
        }
        else
        {
            await Db.KeyExpireAsync(unreadKey, Ttl);
        }
    }

    private static string ListKey(int userId) => $"{ListKeyPrefix}{userId}";

    private static string UnreadKey(int userId) => $"{ListKeyPrefix}{userId}{UnreadKeySuffix}";
}
