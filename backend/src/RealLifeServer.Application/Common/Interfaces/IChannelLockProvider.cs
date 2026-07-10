namespace RealLifeServer.Application.Common.Interfaces;

/// <summary>
/// Distributed lock ensuring a channel's compositor runs on exactly one media node at a time
/// when horizontally scaled. See docs/CONCEPT.md, chapter 11.2. Disposing the returned handle
/// releases the lock; a null return means another node currently owns it.
/// </summary>
public interface IChannelLockProvider
{
    Task<IAsyncDisposable?> TryAcquireAsync(Guid channelId, TimeSpan ttl, CancellationToken ct = default);
}
