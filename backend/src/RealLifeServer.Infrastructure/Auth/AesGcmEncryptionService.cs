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
    private readonly byte[] _key;

    public AesGcmEncryptionService(IOptions<EncryptionSettings> options)
    {
        _key = Convert.FromBase64String(options.Value.MasterKeyBase64);
        if (_key.Length != 32)
        {
            throw new InvalidOperationException("KeyProtection:MasterKeyBase64 must decode to exactly 32 bytes (AES-256).");
        }
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
