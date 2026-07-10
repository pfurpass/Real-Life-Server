using System.Text.Json;
using RealLifeServer.Application.Common.Exceptions;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Application.Streams.StateMachine;
using RealLifeServer.Domain.Entities;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Application.Streams.Services;

/// <summary>
/// The single place that turns a <see cref="SceneTrigger"/> into every side effect described
/// in docs/CONCEPT.md chapter 5.4: persist the new state, manage the stream session lifecycle,
/// write the audit event, drive the compositor, broadcast to dashboards, and notify Discord.
/// Called both from the MediaMTX webhook handlers and from <c>SceneMonitorHostedService</c>.
/// </summary>
public sealed class SceneTransitionService(
    IChannelRepository channelRepository,
    IStreamSessionRepository sessionRepository,
    IApplicationDbContext db,
    SceneStateMachine stateMachine,
    ISceneRuntimeStateStore runtimeState,
    IStreamOrchestrator orchestrator,
    ISceneEventBroadcaster broadcaster,
    IDiscordNotifier discordNotifier,
    IDateTimeProvider clock)
{
    public async Task<SceneTransitionResult> ApplyTriggerAsync(
        Guid channelId,
        SceneTrigger trigger,
        IReadOnlyDictionary<string, object>? metadata = null,
        CancellationToken ct = default)
    {
        var channel = await channelRepository.GetByIdAsync(channelId, ct)
            ?? throw new NotFoundException(nameof(Channel), channelId);

        var context = runtimeState.GetContext(channelId);
        var result = stateMachine.Handle(channel.CurrentSceneState, trigger, channel.SceneThresholds, context);

        if (trigger == SceneTrigger.MetricsRecovered)
        {
            runtimeState.RecordGoodSample(channelId);
        }
        else if (trigger is SceneTrigger.BitrateBelowThreshold or SceneTrigger.PacketLossAboveThreshold)
        {
            runtimeState.ResetGoodSamples(channelId);
        }

        if (!result.Changed)
        {
            return result;
        }

        await ApplySideEffectsAsync(channel, result, trigger, metadata, ct);
        return result;
    }

    /// <summary>
    /// Operator-driven override (e.g. forcing BRB during a break). Unlike <see cref="ApplyTriggerAsync"/>
    /// this bypasses the state machine's guard conditions entirely - an explicit operator decision
    /// is authoritative - but still runs every other side effect (session bookkeeping, audit log,
    /// compositor, broadcast, Discord) so the system stays consistent.
    /// </summary>
    public async Task<SceneTransitionResult> ApplyManualOverrideAsync(Guid channelId, SceneState targetState, CancellationToken ct = default)
    {
        var channel = await channelRepository.GetByIdAsync(channelId, ct)
            ?? throw new NotFoundException(nameof(Channel), channelId);

        var result = new SceneTransitionResult(channel.CurrentSceneState, targetState, channel.CurrentSceneState != targetState, SceneTrigger.ManualOverride);
        if (!result.Changed)
        {
            return result;
        }

        await ApplySideEffectsAsync(channel, result, SceneTrigger.ManualOverride, null, ct);
        return result;
    }

    private async Task ApplySideEffectsAsync(
        Channel channel,
        SceneTransitionResult result,
        SceneTrigger trigger,
        IReadOnlyDictionary<string, object>? metadata,
        CancellationToken ct)
    {
        var now = clock.UtcNow;
        channel.CurrentSceneState = result.NewState;
        channel.UpdatedAt = now;
        runtimeState.RecordStateEntered(channel.Id, result.NewState, now);
        runtimeState.ResetGoodSamples(channel.Id);

        var session = await ManageSessionLifecycleAsync(channel, result, now, ct);

        db.SceneEvents.Add(new SceneEvent
        {
            StreamSessionId = session.Id,
            ChannelId = channel.Id,
            FromState = result.PreviousState,
            ToState = result.NewState,
            Trigger = trigger,
            OccurredAt = now,
            MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata)
        });

        await db.SaveChangesAsync(ct);

        var countdown = result.NewState == SceneState.Reconnecting
            ? TimeSpan.FromSeconds(channel.SceneThresholds.ReconnectTimeoutSeconds)
            : (TimeSpan?)null;

        await orchestrator.ApplySceneAsync(channel.Id, result.NewState, countdown, ct);
        await broadcaster.BroadcastSceneChangedAsync(channel.Id, result.PreviousState, result.NewState, trigger, now);
        await discordNotifier.NotifySceneChangeAsync(channel.Name, result.PreviousState, result.NewState, trigger, ct);
    }

    private async Task<StreamSession> ManageSessionLifecycleAsync(Channel channel, SceneTransitionResult result, DateTimeOffset now, CancellationToken ct)
    {
        var active = await sessionRepository.GetActiveForChannelAsync(channel.Id, ct);

        // First transition out of Offline: start a new session.
        if (result.PreviousState == SceneState.Offline && active is null)
        {
            var session = new StreamSession { ChannelId = channel.Id, StartedAt = now };
            sessionRepository.Add(session);
            return session;
        }

        // Transition into Offline: close the running session.
        if (result.NewState == SceneState.Offline && active is not null)
        {
            active.EndedAt = now;
            active.Status = StreamSessionStatus.Ended;
            active.EndReason = result.PreviousState.ToString();
            return active;
        }

        return active ?? throw new InvalidOperationException(
            $"Channel {channel.Id} transitioned {result.PreviousState}->{result.NewState} without an active session.");
    }
}
