namespace RealLifeServer.Infrastructure.Streaming;

/// <summary>
/// Delay-before-next-attempt schedule shared by <see cref="StreamOrchestrator"/> (deciding
/// whether to try creating a compositor again after it failed to start) and
/// <see cref="ZmqSceneEncoder"/> (deciding whether to retry a failed live/no-live mode-switch
/// restart). A pure calculation so both call sites, and this class's own tests, have exactly one
/// source of truth for "never restart tighter than this" (item 10 - no restart-every-2-seconds
/// loop, no unbounded growth).
/// </summary>
internal static class RestartBackoff
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(16),
        TimeSpan.FromSeconds(30)
    ];

    public static TimeSpan MaxDelay => Delays[^1];

    /// <summary>Delay required before the next attempt, given <paramref name="consecutiveFailures"/> failures so far (0 = no delay, attempt freely).</summary>
    public static TimeSpan DelayFor(int consecutiveFailures)
    {
        if (consecutiveFailures <= 0)
        {
            return TimeSpan.Zero;
        }

        var index = Math.Min(consecutiveFailures - 1, Delays.Length - 1);
        return Delays[index];
    }
}
