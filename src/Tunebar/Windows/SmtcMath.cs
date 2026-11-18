namespace Tunebar.Windows;

/// <summary>Small pure helpers for Windows media session data (kept apart so they can be unit tested).</summary>
internal static class SmtcMath
{
    /// <summary>
    /// Windows only reports the position at <paramref name="lastUpdated"/>. While playing, the real
    /// position is that value plus the time since then. The result is never below 0 and, when the
    /// duration is known, never above it.
    /// </summary>
    internal static double CurrentPosition(
        TimeSpan reported, DateTimeOffset lastUpdated, DateTimeOffset now, bool playing, double? durationSec)
    {
        var seconds = reported.TotalSeconds;
        if (playing)
            seconds += (now - lastUpdated).TotalSeconds;

        seconds = Math.Max(0, seconds);
        return durationSec is { } duration ? Math.Min(seconds, duration) : seconds;
    }
}
