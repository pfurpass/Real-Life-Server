namespace RealLifeServer.Infrastructure.Streaming;

/// <summary>
/// Masks secrets (stream keys, destination RTMP keys) out of an FFmpeg argument list before it
/// is logged. Used by both compositor strategies so the full command line can be logged for
/// diagnostics (item 1) without ever writing a usable credential to the logs.
/// </summary>
internal static class FfmpegArgumentMasking
{
    public static string ToLoggableString(IReadOnlyList<string> arguments) =>
        string.Join(' ', arguments.Select(MaskArgument));

    private static string MaskArgument(string argument) =>
        argument.Contains("://", StringComparison.Ordinal)
            ? string.Join('|', argument.Split('|').Select(MaskUrlLikeTarget))
            : argument;

    /// <summary>
    /// Masks the last path segment of a single URL, which is where this codebase always puts
    /// the secret (".../live/{streamKey}", ".../app/{destinationKey}"). Also handles FFmpeg's
    /// `-f tee` combined target syntax ("[f=flv]url1|[f=flv]url2"), where <paramref name="target"/>
    /// is one already-split '|'-delimited entry that may carry a leading "[...]" tag.
    /// </summary>
    private static string MaskUrlLikeTarget(string target)
    {
        var schemeStart = target.IndexOf("://", StringComparison.Ordinal);
        if (schemeStart < 0)
        {
            return target;
        }

        var tagEnd = target.LastIndexOf(']', schemeStart) + 1; // 0 if there is no leading "[...]" tag
        var tag = target[..tagEnd];
        var url = target[tagEnd..];

        var lastSlash = url.LastIndexOf('/');
        if (lastSlash < 0 || lastSlash == url.Length - 1)
        {
            return target;
        }

        var head = url[..(lastSlash + 1)];
        var secret = url[(lastSlash + 1)..];
        var masked = secret.Length <= 4 ? "****" : $"{secret[..4]}…({secret.Length} chars)";

        return tag + head + masked;
    }
}
