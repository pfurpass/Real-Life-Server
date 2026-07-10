using System.Security.Cryptography;
using System.Text;

namespace RealLifeServer.Application.Common.Utils;

/// <summary>Cryptographically random, URL-safe stream keys. See docs/CONCEPT.md, chapter 10.</summary>
public static class StreamKeyGenerator
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public static string Generate(int length = 32)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        var sb = new StringBuilder(length);
        foreach (var b in bytes)
        {
            sb.Append(Alphabet[b % Alphabet.Length]);
        }
        return sb.ToString();
    }

    public static string Slugify(string name)
    {
        var lowered = name.Trim().ToLowerInvariant();
        var sb = new StringBuilder(lowered.Length);
        var lastWasDash = false;
        foreach (var c in lowered)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                lastWasDash = false;
            }
            else if (!lastWasDash && sb.Length > 0)
            {
                sb.Append('-');
                lastWasDash = true;
            }
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length > 0 ? slug : Generate(8).ToLowerInvariant();
    }
}
