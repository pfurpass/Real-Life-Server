using System.Security.Cryptography;
using System.Text;

namespace RealLifeServer.Application.Common.Utils;

/// <summary>
/// SHA-256 hashing for high-entropy opaque tokens (refresh tokens). Unlike passwords these are
/// already cryptographically random, so a slow KDF (BCrypt) buys nothing - we only need to
/// avoid storing the raw, replayable token value in the database.
/// </summary>
public static class TokenHasher
{
    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
