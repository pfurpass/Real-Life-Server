namespace RealLifeServer.Infrastructure.Auth;

/// <summary>Bound from configuration section "KeyProtection". MasterKey must be 32 bytes, base64-encoded, sourced from ENV/Vault/KMS in production.</summary>
public class EncryptionSettings
{
    public string MasterKeyBase64 { get; set; } = string.Empty;
}
