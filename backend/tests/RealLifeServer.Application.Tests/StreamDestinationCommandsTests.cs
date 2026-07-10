using Microsoft.EntityFrameworkCore;
using RealLifeServer.Application.Channels.Commands;
using RealLifeServer.Application.Common.Exceptions;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;
using Xunit;

namespace RealLifeServer.Application.Tests;

/// <summary>
/// Covers the destination-management commands: RTMP/RTMPS validation, AES encryption of the
/// stream key (never stored or returned in plain text), and the controlled compositor restart
/// (IStreamOrchestrator.StopEncoderAsync, picked up by the next SceneMonitorHostedService tick -
/// see StreamOrchestratorTests) that must fire exactly when a change actually affects the
/// compositor's active output set, and not otherwise.
/// </summary>
public class StreamDestinationCommandsTests
{
    private static Channel CreateChannel() => new() { Id = Guid.NewGuid(), Name = "Test", StreamKey = "ingest-key" };

    [Fact]
    public async Task Add_ValidDestination_EncryptsKeyAndNeverStoresPlainText()
    {
        var channels = new FakeChannelRepository();
        var channel = CreateChannel();
        channels.Seed(channel);
        var orchestrator = new FakeStreamOrchestrator();
        var handler = new AddStreamDestinationCommandHandler(channels, new FakeApplicationDbContext(), new FakeEncryptionService(), orchestrator);

        var dto = await handler.Handle(
            new AddStreamDestinationCommand(channel.Id, StreamPlatform.Twitch, "rtmp://live.twitch.tv/app", "super-secret-key"),
            CancellationToken.None);

        var stored = Assert.Single(channel.Destinations);
        Assert.NotEqual("super-secret-key", stored.StreamKeyEncrypted);
        Assert.DoesNotContain("super-secret-key", stored.StreamKeyEncrypted);

        // The DTO returned to the API/frontend must never carry the key at all, encrypted or not.
        var returned = Assert.Single(dto.Destinations);
        Assert.DoesNotContain("super-secret-key", returned.ToString());
    }

    [Fact]
    public async Task Add_RestartsTheCompositor_SinceNewDestinationsStartEnabled()
    {
        var channels = new FakeChannelRepository();
        var channel = CreateChannel();
        channels.Seed(channel);
        var orchestrator = new FakeStreamOrchestrator();
        var handler = new AddStreamDestinationCommandHandler(channels, new FakeApplicationDbContext(), new FakeEncryptionService(), orchestrator);

        await handler.Handle(new AddStreamDestinationCommand(channel.Id, StreamPlatform.Twitch, "rtmp://live.twitch.tv/app", "key"), CancellationToken.None);

        Assert.Equal(channel.Id, Assert.Single(orchestrator.StoppedChannelIds));
    }

    [Theory]
    [InlineData("http://live.twitch.tv/app")]
    [InlineData("not-a-url")]
    [InlineData("")]
    public async Task Add_InvalidRtmpUrl_ThrowsValidationException(string invalidUrl)
    {
        var channels = new FakeChannelRepository();
        channels.Seed(CreateChannel());
        var handler = new AddStreamDestinationCommandHandler(channels, new FakeApplicationDbContext(), new FakeEncryptionService(), new FakeStreamOrchestrator());

        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new AddStreamDestinationCommand(channels.Single().Id, StreamPlatform.Custom, invalidUrl, "key"), CancellationToken.None));
    }

    [Fact]
    public async Task Add_EmptyStreamKey_ThrowsValidationException()
    {
        var channels = new FakeChannelRepository();
        channels.Seed(CreateChannel());
        var handler = new AddStreamDestinationCommandHandler(channels, new FakeApplicationDbContext(), new FakeEncryptionService(), new FakeStreamOrchestrator());

        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new AddStreamDestinationCommand(channels.Single().Id, StreamPlatform.YouTube, "rtmp://a.rtmp.youtube.com/live2", "  "), CancellationToken.None));
    }

    [Fact]
    public async Task Add_UnknownChannel_ThrowsNotFoundException()
    {
        var handler = new AddStreamDestinationCommandHandler(new FakeChannelRepository(), new FakeApplicationDbContext(), new FakeEncryptionService(), new FakeStreamOrchestrator());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new AddStreamDestinationCommand(Guid.NewGuid(), StreamPlatform.Twitch, "rtmp://live.twitch.tv/app", "key"), CancellationToken.None));
    }

    [Fact]
    public async Task Remove_EnabledDestination_RestartsTheCompositor()
    {
        var channels = new FakeChannelRepository();
        var channel = CreateChannel();
        var destination = new StreamDestination { Id = Guid.NewGuid(), ChannelId = channel.Id, IsEnabled = true, RtmpUrl = "rtmp://x/y", StreamKeyEncrypted = "enc:k" };
        channel.Destinations.Add(destination);
        channels.Seed(channel);
        var orchestrator = new FakeStreamOrchestrator();
        var handler = new RemoveStreamDestinationCommandHandler(channels, new FakeApplicationDbContext(), orchestrator);

        await handler.Handle(new RemoveStreamDestinationCommand(channel.Id, destination.Id), CancellationToken.None);

        Assert.Empty(channel.Destinations);
        Assert.Equal(channel.Id, Assert.Single(orchestrator.StoppedChannelIds));
    }

    [Fact]
    public async Task Remove_DisabledDestination_DoesNotRestartTheCompositor()
    {
        var channels = new FakeChannelRepository();
        var channel = CreateChannel();
        var destination = new StreamDestination { Id = Guid.NewGuid(), ChannelId = channel.Id, IsEnabled = false, RtmpUrl = "rtmp://x/y", StreamKeyEncrypted = "enc:k" };
        channel.Destinations.Add(destination);
        channels.Seed(channel);
        var orchestrator = new FakeStreamOrchestrator();
        var handler = new RemoveStreamDestinationCommandHandler(channels, new FakeApplicationDbContext(), orchestrator);

        await handler.Handle(new RemoveStreamDestinationCommand(channel.Id, destination.Id), CancellationToken.None);

        Assert.Empty(orchestrator.StoppedChannelIds);
    }

    [Fact]
    public async Task Remove_UnknownDestination_ThrowsNotFoundException()
    {
        var channels = new FakeChannelRepository();
        channels.Seed(CreateChannel());
        var handler = new RemoveStreamDestinationCommandHandler(channels, new FakeApplicationDbContext(), new FakeStreamOrchestrator());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new RemoveStreamDestinationCommand(channels.Single().Id, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task SetEnabled_ActualChange_RestartsTheCompositor()
    {
        var channels = new FakeChannelRepository();
        var channel = CreateChannel();
        var destination = new StreamDestination { Id = Guid.NewGuid(), ChannelId = channel.Id, IsEnabled = true, RtmpUrl = "rtmp://x/y", StreamKeyEncrypted = "enc:k" };
        channel.Destinations.Add(destination);
        channels.Seed(channel);
        var orchestrator = new FakeStreamOrchestrator();
        var handler = new SetStreamDestinationEnabledCommandHandler(channels, new FakeApplicationDbContext(), orchestrator);

        var dto = await handler.Handle(new SetStreamDestinationEnabledCommand(channel.Id, destination.Id, false), CancellationToken.None);

        Assert.False(destination.IsEnabled);
        Assert.False(Assert.Single(dto.Destinations).IsEnabled);
        Assert.Equal(channel.Id, Assert.Single(orchestrator.StoppedChannelIds));
    }

    [Fact]
    public async Task SetEnabled_SameValueAsBefore_DoesNotRestartTheCompositor()
    {
        var channels = new FakeChannelRepository();
        var channel = CreateChannel();
        var destination = new StreamDestination { Id = Guid.NewGuid(), ChannelId = channel.Id, IsEnabled = true, RtmpUrl = "rtmp://x/y", StreamKeyEncrypted = "enc:k" };
        channel.Destinations.Add(destination);
        channels.Seed(channel);
        var orchestrator = new FakeStreamOrchestrator();
        var handler = new SetStreamDestinationEnabledCommandHandler(channels, new FakeApplicationDbContext(), orchestrator);

        await handler.Handle(new SetStreamDestinationEnabledCommand(channel.Id, destination.Id, true), CancellationToken.None);

        Assert.Empty(orchestrator.StoppedChannelIds);
    }

    [Fact]
    public async Task ReplaceKey_EnabledDestination_EncryptsAndRestarts()
    {
        var channels = new FakeChannelRepository();
        var channel = CreateChannel();
        var destination = new StreamDestination { Id = Guid.NewGuid(), ChannelId = channel.Id, IsEnabled = true, RtmpUrl = "rtmp://x/y", StreamKeyEncrypted = "enc:old-key" };
        channel.Destinations.Add(destination);
        channels.Seed(channel);
        var orchestrator = new FakeStreamOrchestrator();
        var handler = new ReplaceStreamDestinationKeyCommandHandler(channels, new FakeApplicationDbContext(), new FakeEncryptionService(), orchestrator);

        await handler.Handle(new ReplaceStreamDestinationKeyCommand(channel.Id, destination.Id, "brand-new-key"), CancellationToken.None);

        Assert.NotEqual("enc:old-key", destination.StreamKeyEncrypted);
        Assert.DoesNotContain("brand-new-key", destination.StreamKeyEncrypted, StringComparison.Ordinal);
        Assert.Equal("brand-new-key", new FakeEncryptionService().Decrypt(destination.StreamKeyEncrypted)); // encrypted, but round-trips correctly
        Assert.Equal(channel.Id, Assert.Single(orchestrator.StoppedChannelIds));
    }

    [Fact]
    public async Task ReplaceKey_DisabledDestination_DoesNotRestart()
    {
        var channels = new FakeChannelRepository();
        var channel = CreateChannel();
        var destination = new StreamDestination { Id = Guid.NewGuid(), ChannelId = channel.Id, IsEnabled = false, RtmpUrl = "rtmp://x/y", StreamKeyEncrypted = "enc:old-key" };
        channel.Destinations.Add(destination);
        channels.Seed(channel);
        var orchestrator = new FakeStreamOrchestrator();
        var handler = new ReplaceStreamDestinationKeyCommandHandler(channels, new FakeApplicationDbContext(), new FakeEncryptionService(), orchestrator);

        await handler.Handle(new ReplaceStreamDestinationKeyCommand(channel.Id, destination.Id, "brand-new-key"), CancellationToken.None);

        Assert.Empty(orchestrator.StoppedChannelIds);
    }

    [Fact]
    public async Task ReplaceKey_EmptyNewKey_ThrowsValidationException()
    {
        var channels = new FakeChannelRepository();
        var channel = CreateChannel();
        var destination = new StreamDestination { Id = Guid.NewGuid(), ChannelId = channel.Id, RtmpUrl = "rtmp://x/y", StreamKeyEncrypted = "enc:old" };
        channel.Destinations.Add(destination);
        channels.Seed(channel);
        var handler = new ReplaceStreamDestinationKeyCommandHandler(channels, new FakeApplicationDbContext(), new FakeEncryptionService(), new FakeStreamOrchestrator());

        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new ReplaceStreamDestinationKeyCommand(channel.Id, destination.Id, " "), CancellationToken.None));
    }

    [Fact]
    public async Task Reorder_ValidOrder_AssignsSequentialDisplayOrder()
    {
        var channels = new FakeChannelRepository();
        var channel = CreateChannel();
        var a = new StreamDestination { Id = Guid.NewGuid(), ChannelId = channel.Id, DisplayOrder = 0, RtmpUrl = "rtmp://a", StreamKeyEncrypted = "enc:a" };
        var b = new StreamDestination { Id = Guid.NewGuid(), ChannelId = channel.Id, DisplayOrder = 1, RtmpUrl = "rtmp://b", StreamKeyEncrypted = "enc:b" };
        channel.Destinations.Add(a);
        channel.Destinations.Add(b);
        channels.Seed(channel);
        var handler = new ReorderStreamDestinationsCommandHandler(channels, new FakeApplicationDbContext(), new FakeStreamOrchestrator());

        await handler.Handle(new ReorderStreamDestinationsCommand(channel.Id, [b.Id, a.Id]), CancellationToken.None);

        Assert.Equal((short)0, b.DisplayOrder);
        Assert.Equal((short)1, a.DisplayOrder);
    }

    [Fact]
    public async Task Reorder_MismatchedIds_ThrowsValidationException()
    {
        var channels = new FakeChannelRepository();
        var channel = CreateChannel();
        var a = new StreamDestination { Id = Guid.NewGuid(), ChannelId = channel.Id, RtmpUrl = "rtmp://a", StreamKeyEncrypted = "enc:a" };
        channel.Destinations.Add(a);
        channels.Seed(channel);
        var handler = new ReorderStreamDestinationsCommandHandler(channels, new FakeApplicationDbContext(), new FakeStreamOrchestrator());

        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new ReorderStreamDestinationsCommand(channel.Id, [a.Id, Guid.NewGuid()]), CancellationToken.None));
    }

    [Fact]
    public async Task Reorder_TwoOrMoreEnabledDestinations_RestartsTheCompositor()
    {
        var channels = new FakeChannelRepository();
        var channel = CreateChannel();
        var a = new StreamDestination { Id = Guid.NewGuid(), ChannelId = channel.Id, IsEnabled = true, RtmpUrl = "rtmp://a", StreamKeyEncrypted = "enc:a" };
        var b = new StreamDestination { Id = Guid.NewGuid(), ChannelId = channel.Id, IsEnabled = true, RtmpUrl = "rtmp://b", StreamKeyEncrypted = "enc:b" };
        channel.Destinations.Add(a);
        channel.Destinations.Add(b);
        channels.Seed(channel);
        var orchestrator = new FakeStreamOrchestrator();
        var handler = new ReorderStreamDestinationsCommandHandler(channels, new FakeApplicationDbContext(), orchestrator);

        await handler.Handle(new ReorderStreamDestinationsCommand(channel.Id, [b.Id, a.Id]), CancellationToken.None);

        Assert.Equal(channel.Id, Assert.Single(orchestrator.StoppedChannelIds));
    }

    [Fact]
    public async Task Reorder_FewerThanTwoEnabledDestinations_DoesNotRestart()
    {
        var channels = new FakeChannelRepository();
        var channel = CreateChannel();
        var a = new StreamDestination { Id = Guid.NewGuid(), ChannelId = channel.Id, IsEnabled = true, RtmpUrl = "rtmp://a", StreamKeyEncrypted = "enc:a" };
        var b = new StreamDestination { Id = Guid.NewGuid(), ChannelId = channel.Id, IsEnabled = false, RtmpUrl = "rtmp://b", StreamKeyEncrypted = "enc:b" };
        channel.Destinations.Add(a);
        channel.Destinations.Add(b);
        channels.Seed(channel);
        var orchestrator = new FakeStreamOrchestrator();
        var handler = new ReorderStreamDestinationsCommandHandler(channels, new FakeApplicationDbContext(), orchestrator);

        await handler.Handle(new ReorderStreamDestinationsCommand(channel.Id, [b.Id, a.Id]), CancellationToken.None);

        Assert.Empty(orchestrator.StoppedChannelIds);
    }

    private sealed class FakeChannelRepository : IChannelRepository
    {
        private readonly Dictionary<Guid, Channel> _channels = [];

        public void Seed(Channel channel) => _channels[channel.Id] = channel;
        public Channel Single() => _channels.Values.Single();

        public Task<Channel?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_channels.GetValueOrDefault(id));
        public Task<Channel?> GetByStreamKeyAsync(string streamKey, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Channel?> GetBySlugAsync(string slug, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<Channel>> GetForOwnerAsync(Guid ownerUserId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<Channel>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public void Add(Channel channel) => _channels[channel.Id] = channel;
        public void Remove(Channel channel) => _channels.Remove(channel.Id);
    }

    private sealed class FakeApplicationDbContext : IApplicationDbContext
    {
        public DbSet<User> Users => throw new NotSupportedException();
        public DbSet<Channel> Channels => throw new NotSupportedException();
        public DbSet<StreamDestination> StreamDestinations => throw new NotSupportedException();
        public DbSet<StreamSession> StreamSessions => throw new NotSupportedException();
        public DbSet<SceneEvent> SceneEvents => throw new NotSupportedException();
        public DbSet<StreamMetricSample> StreamMetricSamples => throw new NotSupportedException();
        public DbSet<HeartbeatRecord> HeartbeatRecords => throw new NotSupportedException();
        public DbSet<AuditLogEntry> AuditLogEntries => throw new NotSupportedException();
        public DbSet<RefreshToken> RefreshTokens => throw new NotSupportedException();

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }

    /// <summary>
    /// Base64, not a readable prefix: a fake that just prepended "enc:" would trivially still
    /// contain the plaintext as a substring, which defeats the point of the
    /// Assert.DoesNotContain checks below (the real AesGcmEncryptionService doesn't leak
    /// plaintext into its ciphertext either - see AesGcmEncryptionServiceTests for that).
    /// </summary>
    private sealed class FakeEncryptionService : IEncryptionService
    {
        public string Encrypt(string plainText) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plainText));
        public string Decrypt(string cipherText) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cipherText));
    }

    private sealed class FakeStreamOrchestrator : IStreamOrchestrator
    {
        public List<Guid> StoppedChannelIds { get; } = [];

        public Task EnsureEncoderRunningAsync(Channel channel, IReadOnlyList<StreamDestination> destinations, CancellationToken ct = default) => Task.CompletedTask;
        public Task ApplySceneAsync(Guid channelId, SceneState state, TimeSpan? countdown = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task StopEncoderAsync(Guid channelId, CancellationToken ct = default)
        {
            StoppedChannelIds.Add(channelId);
            return Task.CompletedTask;
        }
        public bool IsEncoderRunning(Guid channelId) => false;
    }
}
