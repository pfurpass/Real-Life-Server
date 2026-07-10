using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using RealLifeServer.Application.Common.Interfaces;

namespace RealLifeServer.Infrastructure.Auth;

/// <summary>
/// AES-256-GCM at-rest encryption for destination stream keys (Twitch/YouTube). See
/// docs/CONCEPT.md, chapter 10. Output layout: base64(nonce[12] || tag[16] || ciphertext).
/// </summary>
public class AesGcmEncryptionService : IEncryptionService
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int RequiredKeyBytes = 32;

    private readonly byte[] _key;

    public AesGcmEncryptionService(IOptions<EncryptionSettings> options)
    {
        _key = ValidateAndDecodeKey(options.Value.MasterKeyBase64);
    }

    /// <summary>
    /// Decodes and validates the configured master key, or throws a precise, actionable
    /// <see cref="InvalidOperationException"/>. Separated out so both the constructor and
    /// <c>Program.cs</c>'s startup check (which forces this to run before any hosted service
    /// starts, instead of on first use inside a retry loop) share one source of truth.
    /// </summary>
    internal static byte[] ValidateAndDecodeKey(string? masterKeyBase64)
    {
        const string howTo = "Generate one with: openssl rand -base64 32";

        if (string.IsNullOrWhiteSpace(masterKeyBase64))
        {
            throw new InvalidOperationException(
                $"KeyProtection:MasterKeyBase64 (env var KEY_PROTECTION_MASTER_KEY) is not set. {howTo}");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(masterKeyBase64.Trim());
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "KeyProtection:MasterKeyBase64 (env var KEY_PROTECTION_MASTER_KEY) is not a valid Base64 string. " +
                $"{howTo}", ex);
        }

        if (key.Length != RequiredKeyBytes)
        {
            throw new InvalidOperationException(
                "KeyProtection:MasterKeyBase64 (env var KEY_PROTECTION_MASTER_KEY) must decode to exactly " +
                $"{RequiredKeyBytes} bytes for AES-256, but decoded to {key.Length} byte(s). {howTo}");
        }

        return key;
    }

    public string Encrypt(string plainText)
    {
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aesGcm = new AesGcm(_key, TagSize);
        aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var result = new byte[NonceSize + TagSize + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(cipherBytes, 0, result, NonceSize + TagSize, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        var data = Convert.FromBase64String(cipherText);
        var nonce = data[..NonceSize];
        var tag = data[NonceSize..(NonceSize + TagSize)];
        var cipherBytes = data[(NonceSize + TagSize)..];
        var plainBytes = new byte[cipherBytes.Length];

        using var aesGcm = new AesGcm(_key, TagSize);
        aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return System.Text.Encoding.UTF8.GetString(plainBytes);
    }
}
