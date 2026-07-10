using RealLifeServer.Application.Common.Interfaces;

namespace RealLifeServer.Infrastructure.Caching;

/// <summary>
/// No-op lock used when no Redis connection string is configured (single-node / local dev).
/// Always "acquires" immediately - correct as long as only one media node exists, which is
/// exactly the case Redis-less deployments are for. See docs/CONCEPT.md, chapter 11.2.
/// </summary>
public class NullChannelLockProvider : IChannelLockProvider
{
    private sealed class NoopLease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public Task<IAsyncDisposable?> TryAcquireAsync(Guid channelId, TimeSpan ttl, CancellationToken ct = default) =>
        Task.FromResult<IAsyncDisposable?>(new NoopLease());
}
