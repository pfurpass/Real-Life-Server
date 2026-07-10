using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Application.Streams.StateMachine;

/// <summary>
/// Pure, side-effect-free scene state machine. See docs/CONCEPT.md, chapter 5.
///
/// This class knows nothing about FFmpeg, the database, or SignalR - it only maps
/// (current state, trigger, thresholds, context) to a new state. All side effects
/// (persisting the new state, sending zmq commands, broadcasting, notifying Discord)
/// are the responsibility of the caller (<c>SceneMonitorHostedService</c>).
/// </summary>
public class SceneStateMachine
{
    public SceneTransitionResult Handle(SceneState current, SceneTrigger trigger, SceneThresholds thresholds, SceneContext context)
    {
        // Global overrides: any state can be force-moved to Offline.
        if (trigger is SceneTrigger.ManualStop or SceneTrigger.ChannelSuspended)
        {
            return Transition(current, SceneState.Offline, trigger);
        }

        return current switch
        {
            SceneState.Offline => HandleOffline(trigger),
            SceneState.Connecting => HandleConnecting(current, trigger, thresholds, context),
            SceneState.Live => HandleLive(current, trigger),
            SceneState.Degraded => HandleDegraded(current, trigger, thresholds, context),
            SceneState.Reconnecting => HandleReconnecting(current, trigger, thresholds, context),
            SceneState.Brb => HandleBrb(current, trigger),
            _ => SceneTransitionResult.Unchanged(current, trigger)
        };
    }

    private SceneTransitionResult HandleOffline(SceneTrigger trigger) => trigger switch
    {
        SceneTrigger.EncoderConnected => Transition(SceneState.Offline, SceneState.Connecting, trigger),
        _ => SceneTransitionResult.Unchanged(SceneState.Offline, trigger)
    };

    private SceneTransitionResult HandleConnecting(SceneState current, SceneTrigger trigger, SceneThresholds thresholds, SceneContext context)
    {
        if (trigger == SceneTrigger.FirstKeyframeReceived)
        {
            return Transition(current, SceneState.Live, trigger);
        }

        if (trigger == SceneTrigger.EncoderDisconnected)
        {
            return Transition(current, SceneState.Reconnecting, trigger);
        }

        if (trigger == SceneTrigger.ConnectTimeoutElapsed &&
            context.TimeInCurrentState >= TimeSpan.FromSeconds(thresholds.ConnectTimeoutSeconds))
        {
            return Transition(current, SceneState.Offline, trigger);
        }

        return SceneTransitionResult.Unchanged(current, trigger);
    }

    private SceneTransitionResult HandleLive(SceneState current, SceneTrigger trigger) => trigger switch
    {
        SceneTrigger.BitrateBelowThreshold or SceneTrigger.PacketLossAboveThreshold =>
            Transition(current, SceneState.Degraded, trigger),
        SceneTrigger.EncoderDisconnected => Transition(current, SceneState.Reconnecting, trigger),
        _ => SceneTransitionResult.Unchanged(current, trigger)
    };

    private SceneTransitionResult HandleDegraded(SceneState current, SceneTrigger trigger, SceneThresholds thresholds, SceneContext context)
    {
        if (trigger == SceneTrigger.EncoderDisconnected)
        {
            return Transition(current, SceneState.Reconnecting, trigger);
        }

        if (trigger == SceneTrigger.MetricsRecovered && context.ConsecutiveGoodSamples >= thresholds.RecoverySampleCount)
        {
            return Transition(current, SceneState.Live, trigger);
        }

        return SceneTransitionResult.Unchanged(current, trigger);
    }

    private SceneTransitionResult HandleReconnecting(SceneState current, SceneTrigger trigger, SceneThresholds thresholds, SceneContext context)
    {
        if (trigger == SceneTrigger.EncoderConnected)
        {
            return Transition(current, SceneState.Connecting, trigger);
        }

        if (trigger == SceneTrigger.ReconnectTimeoutElapsed &&
            context.TimeInCurrentState >= TimeSpan.FromSeconds(thresholds.ReconnectTimeoutSeconds))
        {
            return Transition(current, SceneState.Brb, trigger);
        }

        return SceneTransitionResult.Unchanged(current, trigger);
    }

    private SceneTransitionResult HandleBrb(SceneState current, SceneTrigger trigger) => trigger switch
    {
        SceneTrigger.EncoderConnected => Transition(current, SceneState.Connecting, trigger),
        _ => SceneTransitionResult.Unchanged(current, trigger)
    };

    private static SceneTransitionResult Transition(SceneState from, SceneState to, SceneTrigger trigger) =>
        new(from, to, from != to, trigger);
}
