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

    private IDatabase Db => _redis.GetDatabase(); // Redis veritabani nesnesi

    private TimeSpan Ttl => TimeSpan.FromDays(Math.Max(1, _options.RetentionDays));

    private int MaxPerUser => Math.Max(1, _options.MaxPerUser);

    public async Task AddAsync(NotificationMessage message, CancellationToken cancellationToken = default) //bildirimi redise ekler
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
            SprintId = message.SprintId,
            IsRead = false,
            CreatedAtUtc = message.CreatedAtUtc
        };

        var listKey = ListKey(message.TargetUserId);
        var unreadKey = UnreadKey(message.TargetUserId);
        var payload = JsonSerializer.Serialize(item, JsonOptions);

        //redise yazma islemleri: once listeye ekle, sonra trimle, sonra okunmamis sayisini arttir, sonra TTL'i ayarla
        await Db.ListLeftPushAsync(listKey, payload);
        await Db.ListTrimAsync(listKey, 0, MaxPerUser - 1);
        await Db.KeyExpireAsync(listKey, Ttl);
        await Db.StringIncrementAsync(unreadKey);
        await Db.KeyExpireAsync(unreadKey, Ttl);
    }

    public async Task<IReadOnlyList<NotificationItemDto>> ListAsync(int userId, int take = 30)
    {   //redis'ten kullaniciya ait bildirimleri listele, take parametresi ile maksimum sayiyi sinirla
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

    public async Task<int> MarkReadManyAsync(int userId, IEnumerable<string> notificationIds)
    {
        var idSet = ToIdSet(notificationIds);
        if (idSet.Count == 0)
        {
            return 0;
        }

        var listKey = ListKey(userId);
        var values = await Db.ListRangeAsync(listKey);
        var marked = 0;

        for (var i = 0; i < values.Length; i++)
        {
            if (values[i].IsNullOrEmpty)
            {
                continue;
            }

            var item = JsonSerializer.Deserialize<NotificationItemDto>((string)values[i]!, JsonOptions);
            if (item is null || !idSet.Contains(item.Id) || item.IsRead)
            {
                continue;
            }

            item.IsRead = true;
            await Db.ListSetByIndexAsync(listKey, i, JsonSerializer.Serialize(item, JsonOptions));
            marked++;
        }

        if (marked > 0)
        {
            await AdjustUnreadAsync(userId, -marked);
        }

        return marked;
    }

    public async Task<bool> DeleteAsync(int userId, string notificationId)
    {
        return await DeleteManyAsync(userId, [notificationId]) > 0;
    }

    public async Task<int> DeleteManyAsync(int userId, IEnumerable<string> notificationIds)
    {
        var idSet = ToIdSet(notificationIds);
        if (idSet.Count == 0)
        {
            return 0;
        }

        var listKey = ListKey(userId);
        var values = await Db.ListRangeAsync(listKey);
        if (values.Length == 0)
        {
            return 0;
        }

        var kept = new List<RedisValue>(values.Length);
        var deleted = 0;
        var deletedUnread = 0;

        foreach (var value in values)
        {
            if (value.IsNullOrEmpty)
            {
                continue;
            }

            var item = JsonSerializer.Deserialize<NotificationItemDto>((string)value!, JsonOptions);
            if (item is null)
            {
                continue;
            }

            if (idSet.Contains(item.Id))
            {
                deleted++;
                if (!item.IsRead)
                {
                    deletedUnread++;
                }

                continue;
            }

            kept.Add(value);
        }

        if (deleted == 0)
        {
            return 0;
        }

        await Db.KeyDeleteAsync(listKey);
        if (kept.Count > 0)
        {
            await Db.ListRightPushAsync(listKey, kept.ToArray());
            await Db.KeyExpireAsync(listKey, Ttl);
        }

        if (deletedUnread > 0)
        {
            await AdjustUnreadAsync(userId, -deletedUnread);
        }

        return deleted;
    }

    public async Task ClearAsync(int userId)
    {
        await Db.KeyDeleteAsync([ListKey(userId), UnreadKey(userId)]);
    }

    private async Task DecrementUnreadAsync(int userId)
    {
        await AdjustUnreadAsync(userId, -1);
    }

    private async Task AdjustUnreadAsync(int userId, int delta)
    {
        var unreadKey = UnreadKey(userId);
        if (delta == 0)
        {
            return;
        }

        var next = await Db.StringIncrementAsync(unreadKey, delta);
        if (next < 0)
        {
            await Db.StringSetAsync(unreadKey, 0, Ttl);
            return;
        }

        await Db.KeyExpireAsync(unreadKey, Ttl);
    }

    private static HashSet<string> ToIdSet(IEnumerable<string> notificationIds)
    {
        return notificationIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string ListKey(int userId) => $"{ListKeyPrefix}{userId}"; // kullaniciya ait bildirim listesi keyi

    private static string UnreadKey(int userId) => $"{ListKeyPrefix}{userId}{UnreadKeySuffix}"; // kullaniciya ait okunmamis bildirim sayisi keyi
}
