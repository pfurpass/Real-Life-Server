namespace RealLifeServer.Infrastructure.Auth;

/// <summary>Bound from configuration section "Jwt". Secret must come from ENV/secret manager in production, never source control.</summary>
public class JwtSettings
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "RealLifeServer";
    public string Audience { get; set; } = "RealLifeServer.Clients";
    public int AccessTokenLifetimeMinutes { get; set; } = 15;
}
