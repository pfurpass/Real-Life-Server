using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using RealLifeServer.Infrastructure.Auth;
using Xunit;

namespace RealLifeServer.Infrastructure.Tests;

/// <summary>
/// Covers the KEY_PROTECTION_MASTER_KEY validation added to fix the FormatException that
/// previously surfaced as a repeating background-service crash (docs/CONCEPT.md chapter 10;
/// see the fix in AesGcmEncryptionService.ValidateAndDecodeKey and Program.cs's startup check).
/// </summary>
public class AesGcmEncryptionServiceTests
{
    private static IOptions<EncryptionSettings> CreateOptions(string? masterKeyBase64) =>
        Options.Create(new EncryptionSettings { MasterKeyBase64 = masterKeyBase64! });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingKey_ThrowsWithActionableMessage(string? value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new AesGcmEncryptionService(CreateOptions(value)));

        Assert.Contains("KEY_PROTECTION_MASTER_KEY", ex.Message);
        Assert.Contains("is not set", ex.Message);
    }

    [Fact]
    public void InvalidBase64Key_ThrowsWithActionableMessage()
    {
        // Exactly the mistake that caused the reported bug: the .env.example placeholder
        // "change_me_base64_32_bytes" contains underscores, which are not valid Base64.
        var ex = Assert.Throws<InvalidOperationException>(() => new AesGcmEncryptionService(CreateOptions("change_me_base64_32_bytes")));

        Assert.Contains("KEY_PROTECTION_MASTER_KEY", ex.Message);
        Assert.Contains("not a valid Base64", ex.Message);
        Assert.IsType<FormatException>(ex.InnerException);
    }

    [Fact]
    public void WrongLengthKey_ThrowsWithActionableMessage()
    {
        var sixteenByteKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        var ex = Assert.Throws<InvalidOperationException>(() => new AesGcmEncryptionService(CreateOptions(sixteenByteKey)));

        Assert.Contains("KEY_PROTECTION_MASTER_KEY", ex.Message);
        Assert.Contains("32", ex.Message);
        Assert.Contains("16", ex.Message);
    }

    [Fact]
    public void Valid32ByteKey_ConstructsAndRoundTripsEncryption()
    {
        var validKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var service = new AesGcmEncryptionService(CreateOptions(validKey));

        var cipherText = service.Encrypt("twitch-stream-key-secret");
        var plainText = service.Decrypt(cipherText);

        Assert.Equal("twitch-stream-key-secret", plainText);
        Assert.NotEqual("twitch-stream-key-secret", cipherText);
    }
}
