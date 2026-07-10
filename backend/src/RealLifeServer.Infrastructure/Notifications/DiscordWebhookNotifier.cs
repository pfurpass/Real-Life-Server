using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RealLifeServer.Application.Common.Interfaces;
using RealLifeServer.Domain.Enums;

namespace RealLifeServer.Infrastructure.Notifications;

public class DiscordWebhookNotifier(HttpClient httpClient, IOptions<DiscordOptions> options, ILogger<DiscordWebhookNotifier> logger)
    : IDiscordNotifier
{
    private static readonly HashSet<SceneState> NotableStates = [SceneState.Reconnecting, SceneState.Brb, SceneState.Live, SceneState.Offline, SceneState.Degraded];

    public async Task NotifySceneChangeAsync(string channelName, SceneState fromState, SceneState toState, SceneTrigger trigger, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.Value.WebhookUrl) || !NotableStates.Contains(toState))
        {
            return;
        }

        var (emoji, color) = toState switch
        {
            SceneState.Live => ("🟢", 0x22C55E),
            SceneState.Degraded => ("🟡", 0xEAB308),
            SceneState.Reconnecting => ("🟠", 0xF97316),
            SceneState.Brb => ("🔴", 0xEF4444),
            SceneState.Offline => ("⚫", 0x6B7280),
            _ => ("ℹ️", 0x3B82F6)
        };

        var payload = new
        {
            embeds = new[]
            {
                new
                {
                    title = $"{emoji} {channelName}: {fromState} → {toState}",
                    description = $"Auslöser: `{trigger}`",
                    color,
                    timestamp = DateTimeOffset.UtcNow.ToString("O")
                }
            }
        };

        try
        {
            var response = await httpClient.PostAsJsonAsync(options.Value.WebhookUrl, payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Discord webhook returned {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            // A failed Discord notification must never break the scene transition itself.
            logger.LogWarning(ex, "Failed to send Discord notification");
        }
    }
}
