using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Todoo.Business.Abstract;
using Todoo.Business.Security;

namespace Todoo.Business.Concrete;

/// <summary>
/// Access token'larin (JWT) jti degerlerini Redis'te tutar (allowlist).
/// Her istekte JWT imza dogrulamasina ek olarak jti'nin burada aktif olup olmadigi kontrol edilir.
/// Boylece token'lar suresi dolmadan (logout / sifre sifirlama) aninda gecersiz kilinabilir.
/// </summary>
public class RedisAccessTokenService : IAccessTokenService
{
    private const string AccessKeyPrefix = "todoo:access:";
    private const string UserAccessKeyPrefix = "todoo:user:";
    private const string UserAccessKeySuffix = ":access-sessions";

    private readonly IConnectionMultiplexer _redis;
    private readonly JwtOptions _jwtOptions;

    public RedisAccessTokenService(IConnectionMultiplexer redis, IOptions<JwtOptions> jwtOptions)
    {
        _redis = redis;
        _jwtOptions = jwtOptions.Value;
    }

    private IDatabase Db => _redis.GetDatabase();

    private TimeSpan Ttl => TimeSpan.FromMinutes(_jwtOptions.ExpirationMinutes);

    public async Task<string> IssueAsync(int userId)
    {
        var jti = Guid.NewGuid().ToString("N");

        await Db.StringSetAsync(AccessKeyPrefix + jti, userId, Ttl);
        await Db.SetAddAsync(UserAccessKey(userId), jti);
        await Db.KeyExpireAsync(UserAccessKey(userId), Ttl);

        return jti;
    }

    public async Task<bool> IsActiveAsync(string jti)
    {
        if (string.IsNullOrWhiteSpace(jti))
        {
            return false;
        }

        return await Db.KeyExistsAsync(AccessKeyPrefix + jti);
    }

    public async Task RevokeAsync(string jti)
    {
        if (string.IsNullOrWhiteSpace(jti))
        {
            return;
        }

        var value = await Db.StringGetAsync(AccessKeyPrefix + jti);
        await Db.KeyDeleteAsync(AccessKeyPrefix + jti);

        if (!value.IsNullOrEmpty && int.TryParse((string)value!, out var userId))
        {
            await Db.SetRemoveAsync(UserAccessKey(userId), jti);
        }
    }

    public async Task RevokeAllForUserAsync(int userId)
    {
        var userKey = UserAccessKey(userId);
        var jtis = await Db.SetMembersAsync(userKey);

        if (jtis.Length > 0)
        {
            var keys = jtis.Select(j => (RedisKey)(AccessKeyPrefix + j)).ToArray();
            await Db.KeyDeleteAsync(keys);
        }

        await Db.KeyDeleteAsync(userKey);
    }

    private static string UserAccessKey(int userId) => $"{UserAccessKeyPrefix}{userId}{UserAccessKeySuffix}";
}
