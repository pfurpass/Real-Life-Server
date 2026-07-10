namespace RealLifeServer.Infrastructure.Notifications;

public class DiscordOptions
{
    /// <summary>Empty = notifications disabled.</summary>
    public string WebhookUrl { get; set; } = string.Empty;
}
