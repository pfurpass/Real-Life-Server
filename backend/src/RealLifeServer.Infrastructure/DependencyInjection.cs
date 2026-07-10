using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Infrastructure.Auth;
using RealLifeServer.Infrastructure.Caching;
using RealLifeServer.Infrastructure.Monitoring;
using RealLifeServer.Infrastructure.Notifications;
using RealLifeServer.Infrastructure.Persistence;
using RealLifeServer.Infrastructure.Persistence.Repositories;
using RealLifeServer.Infrastructure.Streaming;
using StackExchange.Redis;

namespace RealLifeServer.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextPool<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres")));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        services.AddScoped<IChannelRepository, ChannelRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IStreamSessionRepository, StreamSessionRepository>();

        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
        services.Configure<EncryptionSettings>(configuration.GetSection("KeyProtection"));
        services.Configure<DiscordOptions>(configuration.GetSection("Discord"));
        services.Configure<MediaMtxOptions>(configuration.GetSection("MediaMtx"));
        services.Configure<SceneAssetOptions>(configuration.GetSection("SceneAssets"));

        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddSingleton<IEncryptionService, AesGcmEncryptionService>();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        services.AddHttpClient<DiscordWebhookNotifier>();
        services.AddScoped<IDiscordNotifier>(sp => sp.GetRequiredService<DiscordWebhookNotifier>());

        services.AddHttpClient<MediaMtxClient>();
        services.AddScoped<IStreamStatsProvider, MediaMtxStatsProvider>();
        services.AddSingleton<ISystemStatsProvider, SystemStatsProvider>();

        services.AddSingleton<IZmqPortAllocator, ZmqPortAllocator>();
        services.AddSingleton<FfmpegCommandBuilder>();
        services.AddSingleton<ISceneEncoderFactory, SceneEncoderFactory>();
        services.AddSingleton<IStreamOrchestrator, StreamOrchestrator>();

        services.AddSingleton<ISceneRuntimeStateStore, InMemorySceneRuntimeStateStore>();

        var redisConnectionString = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
            services.AddSingleton<IChannelLockProvider, RedisChannelLockProvider>();
        }
        else
        {
            services.AddSingleton<IChannelLockProvider, NullChannelLockProvider>();
        }

        return services;
    }
}
