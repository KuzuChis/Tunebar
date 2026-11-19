namespace Tunebar.Core;

/// <summary>
/// Detects that the PC just woke from sleep (a periodic tick arrives much later than expected) and
/// asks the caller to stay away from the taskbar for a while, so we do not poke explorer while it is
/// still waking up. Works for classic sleep and Modern Standby, which suspends desktop apps.
/// </summary>
public sealed class WakeGuard
{
    public static readonly TimeSpan GapThreshold = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(10);

    private DateTimeOffset? _lastTick;
    private DateTimeOffset? _quietUntil;

    /// <summary>Call on every tick; returns true when this tick should do nothing.</summary>
    public bool ShouldSkip(DateTimeOffset now)
    {
        if (_lastTick is { } last && now - last > GapThreshold) _quietUntil = now + QuietPeriod;
        _lastTick = now;
        return _quietUntil is { } until && now < until;
    }
}
