using Tunebar.Core;

namespace Tunebar.Tests;

public class WakeGuardTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Regular_ticks_are_not_skipped()
    {
        var guard = new WakeGuard();
        for (var i = 0; i < 5; i++)
            Assert.False(guard.ShouldSkip(T0.AddSeconds(2 * i)));
    }

    [Fact]
    public void Ticks_right_after_a_long_gap_are_skipped_for_the_quiet_period()
    {
        var guard = new WakeGuard();
        guard.ShouldSkip(T0);

        var woke = T0.AddHours(1);
        Assert.True(guard.ShouldSkip(woke));
        Assert.True(guard.ShouldSkip(woke.AddSeconds(2)));
        Assert.True(guard.ShouldSkip(woke.AddSeconds(8)));
        Assert.False(guard.ShouldSkip(woke.AddSeconds(10)));
        Assert.False(guard.ShouldSkip(woke.AddSeconds(12)));
    }

    [Fact]
    public void First_tick_is_not_skipped()
    {
        Assert.False(new WakeGuard().ShouldSkip(T0));
    }
}
