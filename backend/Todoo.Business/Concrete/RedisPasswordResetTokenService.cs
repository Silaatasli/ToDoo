using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Todoo.Business.Abstract;
using Todoo.Business.Options;

namespace Todoo.Business.Concrete;

public class RedisPasswordResetTokenService : IPasswordResetTokenService
{
    private const string KeyPrefix = "todoo:password-reset:";

    private readonly IConnectionMultiplexer _redis;
    private readonly PasswordResetOptions _options;

    public RedisPasswordResetTokenService(
        IConnectionMultiplexer redis,
        IOptions<PasswordResetOptions> options)
    {
        _redis = redis;
        _options = options.Value;
    }

    private IDatabase Db => _redis.GetDatabase();

    public async Task<string> IssueAsync(int userId, string email)
    {
        var rawToken = GenerateToken();
        var hash = Hash(rawToken);
        var payload = JsonSerializer.Serialize(new Payload { UserId = userId, Email = email });
        var ttl = TimeSpan.FromMinutes(_options.TokenExpirationMinutes);

        await Db.StringSetAsync(KeyPrefix + hash, payload, ttl);
        return rawToken;
    }

    public async Task<(int UserId, string Email)?> ConsumeAsync(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        var hash = Hash(rawToken);
        var key = KeyPrefix + hash;
        var value = await Db.StringGetAsync(key);
        if (value.IsNullOrEmpty)
        {
            return null;
        }

        await Db.KeyDeleteAsync(key);

        var payload = JsonSerializer.Deserialize<Payload>((string)value!);
        if (payload is null)
        {
            return null;
        }

        return (payload.UserId, payload.Email);
    }

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

    private class Payload
    {
        public int UserId { get; set; }

        public string Email { get; set; } = string.Empty;
    }
}
