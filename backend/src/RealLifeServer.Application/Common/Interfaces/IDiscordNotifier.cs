using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Application.Common.Interfaces;

/// <summary>Sends operational alerts to a configured Discord webhook. No-op if not configured.</summary>
public interface IDiscordNotifier
{
    Task NotifySceneChangeAsync(string channelName, SceneState fromState, SceneState toState, SceneTrigger trigger, CancellationToken ct = default);
}
