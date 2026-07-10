using Microsoft.Extensions.Options;
using RealLifeServer.Domain.Entities;
using RealLifeServer.Infrastructure.Streaming;
using Xunit;

namespace RealLifeServer.Infrastructure.Tests;

/// <summary>
/// Regression coverage for the reported filter-graph bugs: `ovBrb` was declared in
/// <see cref="CompositorPlan"/> but never created in the filter graph, and the node that
/// actually controlled the BRB layer was misnamed `ovLive`. These tests assert generically that
/// every name <see cref="FfmpegCommandBuilder.BuildZmqCompositor"/> hands back in the plan
/// actually exists as a real `@name` filter instance in the generated graph, and cover the
/// LiveInput/NoLiveInput mode split (item 6/7: NoLiveInput mode must never reference RTSP).
/// </summary>
public class FfmpegCommandBuilderTests
{
    private static FfmpegCommandBuilder CreateBuilder(out SceneAssetOptions assets, out MediaMtxOptions mediaMtx)
    {
        mediaMtx = new MediaMtxOptions { RtspBaseUrl = "rtsp://mediamtx:8554", PathPrefix = "live" };
        assets = new SceneAssetOptions
        {
            BrbVideoPath = "/assets/scenes/brb.mp4",
            ReconnectingVideoPath = "/assets/scenes/reconnecting.mp4",
            OfflineVideoPath = "/assets/scenes/offline.mp4",
            CanvasWidth = 1280,
            CanvasHeight = 720
        };
        return new FfmpegCommandBuilder(Options.Create(mediaMtx), Options.Create(assets));
    }

    private static Channel CreateChannel(string streamKey = "abc123secretkey") => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Channel",
        StreamKey = streamKey
    };

    private static string FilterComplexArgument(CompositorPlan plan)
    {
        var index = plan.Arguments.ToList().IndexOf("-filter_complex");
        Assert.True(index >= 0, "Expected a -filter_complex argument.");
        return plan.Arguments[index + 1];
    }

    [Fact]
    public void LiveInputMode_EveryPlanFilterNameExistsInTheGraph()
    {
        var builder = CreateBuilder(out _, out _);
        var plan = builder.BuildZmqCompositor(CreateChannel(), [], zmqPort: 15000, includeLiveInput: true);
        var filter = FilterComplexArgument(plan);

        AssertFilterNamesExist(plan, filter);
    }

    [Fact]
    public void NoLiveInputMode_EveryPlanFilterNameExistsInTheGraph()
    {
        var builder = CreateBuilder(out _, out _);
        var plan = builder.BuildZmqCompositor(CreateChannel(), [], zmqPort: 15001, includeLiveInput: false);
        var filter = FilterComplexArgument(plan);

        AssertFilterNamesExist(plan, filter);
    }

    private static void AssertFilterNamesExist(CompositorPlan plan, string filter)
    {
        // overlay@name= and drawtext@name= are the only two filter types this graph names.
        Assert.Contains($"overlay@{plan.BrbOverlayName}=", filter);
        Assert.Contains($"overlay@{plan.ReconnectingOverlayName}=", filter);
        Assert.Contains($"overlay@{plan.OfflineOverlayName}=", filter);
        Assert.Contains($"drawtext@{plan.CountdownTextName}=", filter);

        // Every "@name" instance declared in the graph must be one of the plan's own names -
        // guards against a future edit reintroducing an orphan name like the original "ovLive".
        var declaredNames = System.Text.RegularExpressions.Regex.Matches(filter, @"@(\w+)=")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
        var planNames = new HashSet<string> { plan.BrbOverlayName, plan.ReconnectingOverlayName, plan.OfflineOverlayName, plan.CountdownTextName };
        Assert.True(planNames.SetEquals(declaredNames),
            $"Plan names [{string.Join(", ", planNames)}] must exactly match graph-declared names [{string.Join(", ", declaredNames)}]");
    }

    [Fact]
    public void LiveInputMode_NeverReferencesALiveOverlayName()
    {
        // Regression guard for the specific reported bug: there is no separate "live" toggle
        // node any more (live is the base layer, always visible unless another layer is
        // overlaid on top of it) - a name like "ovLive" must never appear.
        var builder = CreateBuilder(out _, out _);
        var plan = builder.BuildZmqCompositor(CreateChannel(), [], zmqPort: 15002, includeLiveInput: true);
        var filter = FilterComplexArgument(plan);

        Assert.DoesNotContain("ovLive", filter);
        Assert.DoesNotContain("ovLive", plan.BrbOverlayName);
        Assert.DoesNotContain("ovLive", plan.ReconnectingOverlayName);
        Assert.DoesNotContain("ovLive", plan.OfflineOverlayName);
    }

    [Fact]
    public void LiveInputMode_IncludesTheRtspInputBuiltFromTheStreamKey()
    {
        var builder = CreateBuilder(out _, out var mediaMtx);
        var channel = CreateChannel("abc123secretkey");
        var plan = builder.BuildZmqCompositor(channel, [], zmqPort: 15003, includeLiveInput: true);

        Assert.True(plan.HasLiveInput);
        Assert.Contains($"{mediaMtx.RtspBaseUrl}/{mediaMtx.PathPrefix}/{channel.StreamKey}", plan.Arguments);
    }

    [Fact]
    public void NoLiveInputMode_NeverOpensAnRtspConnection()
    {
        // The actual root cause of the reported crash loop: this mode must be able to run
        // indefinitely with no publisher, so it must never attempt to open the live RTSP path.
        var builder = CreateBuilder(out _, out _);
        var plan = builder.BuildZmqCompositor(CreateChannel(), [], zmqPort: 15004, includeLiveInput: false);

        Assert.False(plan.HasLiveInput);
        Assert.DoesNotContain(plan.Arguments, a => a.Contains("rtsp://", StringComparison.Ordinal));
        Assert.DoesNotContain("-rtsp_transport", plan.Arguments); // no rtsp-specific flags at all
    }

    [Fact]
    public void NoLiveInputMode_UsesALavfiFillerAndSilentAudioInsteadOfLive()
    {
        var builder = CreateBuilder(out _, out _);
        var plan = builder.BuildZmqCompositor(CreateChannel(), [], zmqPort: 15005, includeLiveInput: false);

        Assert.Contains(plan.Arguments, a => a.StartsWith("color=c=black", StringComparison.Ordinal));
        Assert.Contains(plan.Arguments, a => a.StartsWith("anullsrc", StringComparison.Ordinal));
    }

    [Fact]
    public void NoDestinations_UsesNullMuxer()
    {
        var builder = CreateBuilder(out _, out _);
        var plan = builder.BuildZmqCompositor(CreateChannel(), [], zmqPort: 15006, includeLiveInput: false);

        Assert.Contains("null", plan.Arguments);
    }

    [Fact]
    public void MultipleDestinations_UsesTeeMuxerWithBothTargets()
    {
        var builder = CreateBuilder(out _, out _);
        var destinations = new[] { "rtmp://live.twitch.tv/app/twitchkey", "rtmp://a.rtmp.youtube.com/live2/youtubekey" };
        var plan = builder.BuildZmqCompositor(CreateChannel(), destinations, zmqPort: 15007, includeLiveInput: true);

        var teeArgIndex = plan.Arguments.ToList().IndexOf("tee");
        Assert.True(teeArgIndex >= 0);
        var teeTarget = plan.Arguments[teeArgIndex + 1];
        Assert.Contains(destinations[0], teeTarget);
        Assert.Contains(destinations[1], teeTarget);
    }
}
