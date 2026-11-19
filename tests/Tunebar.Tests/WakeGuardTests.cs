using Tunebar.Core;

namespace Tunebar.Tests;

public class WakeGuardTests
{
    private static readonly TimeSpan T0 = TimeSpan.FromMinutes(5);

    [Fact]
    public void Regular_ticks_are_not_skipped()
    {
        var guard = new WakeGuard();
        for (var i = 0; i < 5; i++)
            Assert.False(guard.ShouldSkip(T0 + TimeSpan.FromSeconds(2 * i)));
    }

    [Fact]
    public void Ticks_right_after_a_long_gap_are_skipped_for_the_quiet_period()
    {
        var guard = new WakeGuard();
        guard.ShouldSkip(T0);

        var woke = T0 + TimeSpan.FromHours(1);
        Assert.True(guard.ShouldSkip(woke));
        Assert.True(guard.ShouldSkip(woke + TimeSpan.FromSeconds(2)));
        Assert.True(guard.ShouldSkip(woke + TimeSpan.FromSeconds(8)));
        Assert.False(guard.ShouldSkip(woke + TimeSpan.FromSeconds(10)));
        Assert.False(guard.ShouldSkip(woke + TimeSpan.FromSeconds(12)));
    }

    [Fact]
    public void First_tick_is_not_skipped()
    {
        Assert.False(new WakeGuard().ShouldSkip(T0));
    }

    [Fact]
    public void Ticks_seen_while_the_caller_was_busy_do_not_look_like_a_gap()
    {
        // The host reports every tick to the guard, busy or not, so a 15 s taskbar read
        // (ticks every 2 s all along) never produces a gap above the threshold.
        var guard = new WakeGuard();
        for (var i = 0; i < 8; i++)
            Assert.False(guard.ShouldSkip(T0 + TimeSpan.FromSeconds(2 * i)));
        Assert.False(guard.ShouldSkip(T0 + TimeSpan.FromSeconds(16)));
    }
}
