using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Infrastructure.Streaming;

public class SceneEncoderFactory(IServiceProvider serviceProvider) : ISceneEncoderFactory
{
    public ISceneEncoder Create(Channel channel)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        return channel.CompositorStrategy switch
        {
            CompositorStrategy.Zmq => new ZmqSceneEncoder(
                channel.Id,
                serviceProvider.GetRequiredService<FfmpegCommandBuilder>(),
                serviceProvider.GetRequiredService<IZmqPortAllocator>(),
                loggerFactory),

            CompositorStrategy.RestartableFallback => new RestartableFallbackSceneEncoder(
                channel.Id,
                serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MediaMtxOptions>>(),
                serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SceneAssetOptions>>(),
                loggerFactory),

            _ => throw new NotSupportedException($"Unknown compositor strategy: {channel.CompositorStrategy}")
        };
    }
}
