using RealLifeServer.Application.Streams.StateMachine;
using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;
using Xunit;

namespace RealLifeServer.Application.Tests;

/// <summary>
/// Covers the transition table in docs/CONCEPT.md, chapter 5.2/5.4. The state machine is pure,
/// so these tests need no mocks, no database, no FFmpeg.
/// </summary>
public class SceneStateMachineTests
{
    private readonly SceneStateMachine _sut = new();
    private readonly SceneThresholds _thresholds = new();

    private static SceneContext Ctx(TimeSpan? timeInState = null, int goodSamples = 0) =>
        new(timeInState ?? TimeSpan.Zero, goodSamples);

    [Fact]
    public void Offline_EncoderConnected_MovesToConnecting()
    {
        var result = _sut.Handle(SceneState.Offline, SceneTrigger.EncoderConnected, _thresholds, Ctx());

        Assert.True(result.Changed);
        Assert.Equal(SceneState.Connecting, result.NewState);
    }

    [Fact]
    public void Connecting_FirstKeyframe_MovesToLive()
    {
        var result = _sut.Handle(SceneState.Connecting, SceneTrigger.FirstKeyframeReceived, _thresholds, Ctx());

        Assert.True(result.Changed);
        Assert.Equal(SceneState.Live, result.NewState);
    }

    [Fact]
    public void Connecting_ConnectTimeoutBeforeThreshold_DoesNotTransition()
    {
        var context = Ctx(TimeSpan.FromSeconds(_thresholds.ConnectTimeoutSeconds - 1));
        var result = _sut.Handle(SceneState.Connecting, SceneTrigger.ConnectTimeoutElapsed, _thresholds, context);

        Assert.False(result.Changed);
    }

    [Fact]
    public void Connecting_ConnectTimeoutAfterThreshold_MovesToOffline()
    {
        var context = Ctx(TimeSpan.FromSeconds(_thresholds.ConnectTimeoutSeconds));
        var result = _sut.Handle(SceneState.Connecting, SceneTrigger.ConnectTimeoutElapsed, _thresholds, context);

        Assert.True(result.Changed);
        Assert.Equal(SceneState.Offline, result.NewState);
    }

    [Theory]
    [InlineData(SceneTrigger.BitrateBelowThreshold)]
    [InlineData(SceneTrigger.PacketLossAboveThreshold)]
    public void Live_QualityIssue_MovesToDegraded(SceneTrigger trigger)
    {
        var result = _sut.Handle(SceneState.Live, trigger, _thresholds, Ctx());

        Assert.True(result.Changed);
        Assert.Equal(SceneState.Degraded, result.NewState);
    }

    [Fact]
    public void Live_EncoderDisconnected_MovesToReconnecting()
    {
        var result = _sut.Handle(SceneState.Live, SceneTrigger.EncoderDisconnected, _thresholds, Ctx());

        Assert.True(result.Changed);
        Assert.Equal(SceneState.Reconnecting, result.NewState);
    }

    [Fact]
    public void Degraded_MetricsRecoveredBelowSampleCount_StaysDegraded()
    {
        var context = Ctx(goodSamples: _thresholds.RecoverySampleCount - 1);
        var result = _sut.Handle(SceneState.Degraded, SceneTrigger.MetricsRecovered, _thresholds, context);

        Assert.False(result.Changed);
    }

    [Fact]
    public void Degraded_MetricsRecoveredAtSampleCount_MovesToLive()
    {
        var context = Ctx(goodSamples: _thresholds.RecoverySampleCount);
        var result = _sut.Handle(SceneState.Degraded, SceneTrigger.MetricsRecovered, _thresholds, context);

        Assert.True(result.Changed);
        Assert.Equal(SceneState.Live, result.NewState);
    }

    [Fact]
    public void Degraded_EncoderDisconnected_MovesToReconnecting()
    {
        var result = _sut.Handle(SceneState.Degraded, SceneTrigger.EncoderDisconnected, _thresholds, Ctx());

        Assert.True(result.Changed);
        Assert.Equal(SceneState.Reconnecting, result.NewState);
    }

    [Fact]
    public void Reconnecting_EncoderConnected_MovesToConnecting()
    {
        var result = _sut.Handle(SceneState.Reconnecting, SceneTrigger.EncoderConnected, _thresholds, Ctx());

        Assert.True(result.Changed);
        Assert.Equal(SceneState.Connecting, result.NewState);
    }

    [Fact]
    public void Reconnecting_TimeoutBeforeThreshold_DoesNotTransition()
    {
        var context = Ctx(TimeSpan.FromSeconds(_thresholds.ReconnectTimeoutSeconds - 1));
        var result = _sut.Handle(SceneState.Reconnecting, SceneTrigger.ReconnectTimeoutElapsed, _thresholds, context);

        Assert.False(result.Changed);
    }

    [Fact]
    public void Reconnecting_TimeoutAfterThreshold_MovesToBrb()
    {
        var context = Ctx(TimeSpan.FromSeconds(_thresholds.ReconnectTimeoutSeconds));
        var result = _sut.Handle(SceneState.Reconnecting, SceneTrigger.ReconnectTimeoutElapsed, _thresholds, context);

        Assert.True(result.Changed);
        Assert.Equal(SceneState.Brb, result.NewState);
    }

    [Fact]
    public void Brb_EncoderConnected_MovesToConnecting()
    {
        var result = _sut.Handle(SceneState.Brb, SceneTrigger.EncoderConnected, _thresholds, Ctx());

        Assert.True(result.Changed);
        Assert.Equal(SceneState.Connecting, result.NewState);
    }

    [Theory]
    [InlineData(SceneState.Offline)]
    [InlineData(SceneState.Connecting)]
    [InlineData(SceneState.Live)]
    [InlineData(SceneState.Degraded)]
    [InlineData(SceneState.Reconnecting)]
    [InlineData(SceneState.Brb)]
    public void AnyState_ManualStop_MovesToOffline(SceneState current)
    {
        var result = _sut.Handle(current, SceneTrigger.ManualStop, _thresholds, Ctx());

        Assert.Equal(SceneState.Offline, result.NewState);
        Assert.Equal(current != SceneState.Offline, result.Changed);
    }

    [Fact]
    public void AnyState_ChannelSuspended_MovesToOffline()
    {
        var result = _sut.Handle(SceneState.Live, SceneTrigger.ChannelSuspended, _thresholds, Ctx());

        Assert.True(result.Changed);
        Assert.Equal(SceneState.Offline, result.NewState);
    }

    [Fact]
    public void Live_NeverLosesViewerConnection_ReconnectingAndBrbAreVisualOnlyStates()
    {
        // Regression guard for the core product promise (docs/CONCEPT.md chapter 1/3): losing
        // the encoder must never route through Offline directly - it always passes through
        // Reconnecting/BRB first, which keeps the compositor (and therefore the outbound
        // RTMP connection to Twitch/YouTube) alive.
        var reconnecting = _sut.Handle(SceneState.Live, SceneTrigger.EncoderDisconnected, _thresholds, Ctx());
        Assert.Equal(SceneState.Reconnecting, reconnecting.NewState);

        var brb = _sut.Handle(SceneState.Reconnecting, SceneTrigger.ReconnectTimeoutElapsed, _thresholds,
            Ctx(TimeSpan.FromSeconds(_thresholds.ReconnectTimeoutSeconds)));
        Assert.Equal(SceneState.Brb, brb.NewState);
        Assert.NotEqual(SceneState.Offline, brb.NewState);
    }
}
