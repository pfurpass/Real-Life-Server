using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Application.Common.Interfaces;

/// <summary>
/// Controls the compositor process for a single channel: starts it once and, from then on,
/// only switches the visible scene layer. See docs/CONCEPT.md chapter 4.
/// </summary>
public interface ISceneEncoder : IAsyncDisposable
{
    Guid ChannelId { get; }
    bool IsRunning { get; }

    /// <summary>Raised when the underlying FFmpeg process exits without <see cref="StopAsync"/> having been called - a crash the orchestrator should react to.</summary>
    event Action<Guid, int>? ProcessExitedUnexpectedly;

    /// <summary>
    /// <paramref name="destinationRtmpUrls"/> are fully resolved, ready-to-publish RTMP URLs
    /// (base URL + decrypted stream key already joined). Decryption happens one layer up, in
    /// the orchestrator, so this interface never needs to know about <c>IEncryptionService</c>.
    /// </summary>
    Task StartAsync(Channel channel, IReadOnlyList<string> destinationRtmpUrls, CancellationToken ct = default);

    /// <summary>
    /// Switches the visible scene layer. RestartableFallback always restarts the process.
    /// Zmq restarts the process only when the target state's live-input requirement changes
    /// (Live/Degraded vs. everything else) - MediaMTX does not let it hold an RTSP input open
    /// with no publisher, and FFmpeg cannot add/remove that input without a restart; all other
    /// transitions are runtime zmq commands with no interruption to the outbound connection.
    /// See docs/CONCEPT.md chapter 4.1.
    /// </summary>
    Task ApplySceneAsync(SceneState state, TimeSpan? countdown = null, CancellationToken ct = default);

    Task StopAsync(CancellationToken ct = default);
}

/// <summary>Creates the correct <see cref="ISceneEncoder"/> implementation for a channel's configured strategy.</summary>
public interface ISceneEncoderFactory
{
    ISceneEncoder Create(Channel channel);
}
