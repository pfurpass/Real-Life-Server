using RealLifeServer.Domain.Enums;
using RealLifeServer.Infrastructure.Streaming;
using Xunit;

namespace RealLifeServer.Infrastructure.Tests;

/// <summary>
/// The pure state-&gt;mode decision behind the compositor's two-mode strategy (docs/CONCEPT.md
/// chapter 4.1): only Live/Degraded ever justify opening the RTSP input. Covers item 12's "kein
/// Publisher beim Start" (Offline/Connecting), "Publisher vorhanden" (Live/Degraded), and
/// "Publisher verschwindet" (Reconnecting/Brb) at the decision-logic level.
/// </summary>
public class ZmqSceneEncoderModeTests
{
    [Theory]
    [InlineData(SceneState.Live, true)]
    [InlineData(SceneState.Degraded, true)]
    [InlineData(SceneState.Offline, false)]
    [InlineData(SceneState.Connecting, false)]
    [InlineData(SceneState.Reconnecting, false)]
    [InlineData(SceneState.Brb, false)]
    public void RequiresLiveInput_MatchesExpectedMode(SceneState state, bool expected)
    {
        Assert.Equal(expected, ZmqSceneEncoder.RequiresLiveInput(state));
    }
}
