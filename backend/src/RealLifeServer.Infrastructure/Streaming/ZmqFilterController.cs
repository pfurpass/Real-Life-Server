using Microsoft.Extensions.Logging;
using NetMQ;
using NetMQ.Sockets;

namespace RealLifeServer.Infrastructure.Streaming;

/// <summary>
/// Talks to a single running FFmpeg compositor process via its `zmq` filter (a ZeroMQ REQ/REP
/// endpoint FFmpeg exposes for runtime filter-graph commands). This is what lets
/// <see cref="ZmqSceneEncoder"/> switch the visible scene layer without ever restarting the
/// FFmpeg process or its outbound RTMP connection. See docs/CONCEPT.md, chapter 4.1.
/// </summary>
public sealed class ZmqFilterController(int port, ILogger logger) : IAsyncDisposable
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(2);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private RequestSocket? _socket;

    private RequestSocket Socket => _socket ??= CreateSocket();

    private RequestSocket CreateSocket()
    {
        var socket = new RequestSocket();
        socket.Connect($"tcp://127.0.0.1:{port}");
        return socket;
    }

    /// <summary>Sends "&lt;target&gt; &lt;command&gt; &lt;arg&gt;" to the named filter instance, e.g. "ovLive enable 1".</summary>
    public async Task SendCommandAsync(string target, string command, string arg, CancellationToken ct = default)
    {
        var message = $"{target} {command} {arg}";

        await _lock.WaitAsync(ct);
        try
        {
            await Task.Run(() =>
            {
                if (!Socket.TrySendFrame(CommandTimeout, message))
                {
                    throw new TimeoutException($"zmq command to port {port} timed out: {message}");
                }

                if (!Socket.TryReceiveFrameString(CommandTimeout, out var reply))
                {
                    throw new TimeoutException($"zmq reply from port {port} timed out for: {message}");
                }

                logger.LogDebug("zmq[{Port}] {Message} -> {Reply}", port, message, reply);
            }, ct);
        }
        catch (Exception ex)
        {
            // A stuck REQ/REP socket after a failed exchange must not poison future calls -
            // recreate it lazily on next use.
            logger.LogWarning(ex, "zmq command failed on port {Port}, resetting socket", port);
            ResetSocket();
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task SetOverlayEnabledAsync(string overlayName, bool enabled, CancellationToken ct = default) =>
        SendCommandAsync(overlayName, "enable", enabled ? "1" : "0", ct);

    public Task UpdateCountdownTextAsync(string drawTextName, string text, CancellationToken ct = default) =>
        SendCommandAsync(drawTextName, "reinit", $"text='{EscapeForFilterGraph(text)}'", ct);

    private static string EscapeForFilterGraph(string text) =>
        text.Replace("\\", "\\\\").Replace(":", "\\:").Replace("'", "\\'");

    private void ResetSocket()
    {
        _socket?.Dispose();
        _socket = null;
    }

    public ValueTask DisposeAsync()
    {
        _socket?.Dispose();
        _lock.Dispose();
        return ValueTask.CompletedTask;
    }
}
