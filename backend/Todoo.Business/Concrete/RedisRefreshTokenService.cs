using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Todoo.Business.Abstract;
using Todoo.Business.Models;
using Todoo.Business.Security;

namespace Todoo.Business.Concrete;

/// <summary>
/// Refresh token'lari (= kullanici oturumlarini) Redis uzerinde tutar.
/// Her refresh token bir "session" gibi davranir: kullanici basina birden fazla
/// aktif token olabilir (farkli cihaz/tarayicilar), tumu tek seferde iptal edilebilir.
/// </summary>
public class RedisRefreshTokenService : IRefreshTokenService
{
    private const string RefreshKeyPrefix = "todoo:refresh:";
    private const string UserTokensKeyPrefix = "todoo:user:";
    private const string UserTokensKeySuffix = ":sessions";

    private readonly IConnectionMultiplexer _redis;
    private readonly JwtOptions _jwtOptions;

    public RedisRefreshTokenService(IConnectionMultiplexer redis, IOptions<JwtOptions> jwtOptions)
    {
        _redis = redis;
        _jwtOptions = jwtOptions.Value;
    }

    private IDatabase Db => _redis.GetDatabase();

    public async Task<string> IssueAsync(int userId, string email)
    {
        var rawToken = GenerateToken();
        await StoreAsync(rawToken, userId, email);
        return rawToken;
    }

    public async Task<RefreshTokenRotationResult?> ValidateAndRotateAsync(string refreshToken)
    {
        var hash = Hash(refreshToken);
        var value = await Db.StringGetAsync(RefreshKeyPrefix + hash);
        if (value.IsNullOrEmpty)
        {
            return null;
        }

        var payload = JsonSerializer.Deserialize<RefreshTokenPayload>((string)value!);
        if (payload is null)
        {
            return null;
        }

        await Db.KeyDeleteAsync(RefreshKeyPrefix + hash);
        await Db.SetRemoveAsync(UserSessionsKey(payload.UserId), hash);

        var newToken = GenerateToken();
        await StoreAsync(newToken, payload.UserId, payload.Email);

        return new RefreshTokenRotationResult
        {
            UserId = payload.UserId,
            Email = payload.Email,
            NewRefreshToken = newToken
        };
    }

    public async Task RevokeAsync(string refreshToken)
    {
        var hash = Hash(refreshToken);
        var value = await Db.StringGetAsync(RefreshKeyPrefix + hash);
        await Db.KeyDeleteAsync(RefreshKeyPrefix + hash);

        if (value.IsNullOrEmpty)
        {
            return;
        }

        var payload = JsonSerializer.Deserialize<RefreshTokenPayload>((string)value!);
        if (payload is not null)
        {
            await Db.SetRemoveAsync(UserSessionsKey(payload.UserId), hash);
        }
    }

    public async Task RevokeAllForUserAsync(int userId)
    {
        var sessionsKey = UserSessionsKey(userId);
        var hashes = await Db.SetMembersAsync(sessionsKey);

        if (hashes.Length > 0)
        {
            var keys = hashes.Select(h => (RedisKey)(RefreshKeyPrefix + h)).ToArray();
            await Db.KeyDeleteAsync(keys);
        }

        await Db.KeyDeleteAsync(sessionsKey);
    }

    private async Task StoreAsync(string rawToken, int userId, string email)
    {
        var hash = Hash(rawToken);
        var payload = JsonSerializer.Serialize(new RefreshTokenPayload { UserId = userId, Email = email });
        var ttl = TimeSpan.FromDays(_jwtOptions.RefreshTokenExpirationDays);

        await Db.StringSetAsync(RefreshKeyPrefix + hash, payload, ttl);
        await Db.SetAddAsync(UserSessionsKey(userId), hash);
        await Db.KeyExpireAsync(UserSessionsKey(userId), ttl);
    }

    private static string UserSessionsKey(int userId) => $"{UserTokensKeyPrefix}{userId}{UserTokensKeySuffix}";

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    private static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private class RefreshTokenPayload
    {
        public int UserId { get; set; }

        public string Email { get; set; } = string.Empty;
    }
}
