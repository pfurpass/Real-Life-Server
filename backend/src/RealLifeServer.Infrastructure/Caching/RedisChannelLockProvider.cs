using Microsoft.Extensions.Logging;
using RealLifeServer.Application.Common.Interfaces;
using StackExchange.Redis;

namespace RealLifeServer.Infrastructure.Caching;

public class RedisChannelLockProvider(IConnectionMultiplexer redis, ILogger<RedisChannelLockProvider> logger) : IChannelLockProvider
{
    private const string KeyPrefix = "rls:channel-lock:";

    public async Task<IAsyncDisposable?> TryAcquireAsync(Guid channelId, TimeSpan ttl, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var key = KeyPrefix + channelId;
        var token = Guid.NewGuid().ToString("N");

        var acquired = await db.StringSetAsync(key, token, ttl, When.NotExists);
        if (!acquired)
        {
            logger.LogDebug("Channel {ChannelId} is already owned by another media node", channelId);
            return null;
        }

        return new Lease(db, key, token);
    }

    private sealed class Lease(IDatabase db, string key, string token) : IAsyncDisposable
    {
        // Release only if we still hold the lock (compare-and-delete via a small Lua script)
        // to avoid releasing a lock some other node acquired after our TTL expired.
        private const string ReleaseScript = """
            if redis.call("get", KEYS[1]) == ARGV[1] then
                return redis.call("del", KEYS[1])
            else
                return 0
            end
            """;

        public async ValueTask DisposeAsync()
        {
            await db.ScriptEvaluateAsync(ReleaseScript, [key], [token]);
        }
    }
}
