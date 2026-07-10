using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace RealLifeServer.Infrastructure.Streaming;

/// <summary>Hands out one local TCP port per running compositor for its zmq control socket.</summary>
public interface IZmqPortAllocator
{
    int Allocate(Guid channelId);
    void Release(Guid channelId);
}

public class ZmqPortAllocator(IOptions<SceneAssetOptions> options) : IZmqPortAllocator
{
    private readonly ConcurrentDictionary<Guid, int> _allocations = new();
    private readonly ConcurrentDictionary<int, Guid> _portsInUse = new();
    private readonly object _gate = new();

    public int Allocate(Guid channelId)
    {
        if (_allocations.TryGetValue(channelId, out var existing))
        {
            return existing;
        }

        lock (_gate)
        {
            if (_allocations.TryGetValue(channelId, out existing))
            {
                return existing;
            }

            var port = options.Value.ZmqBasePort;
            while (_portsInUse.ContainsKey(port))
            {
                port++;
            }

            _portsInUse[port] = channelId;
            _allocations[channelId] = port;
            return port;
        }
    }

    public void Release(Guid channelId)
    {
        if (_allocations.TryRemove(channelId, out var port))
        {
            _portsInUse.TryRemove(port, out _);
        }
    }
}
