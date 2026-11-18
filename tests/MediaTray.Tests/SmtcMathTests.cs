using MediaTray.Windows;

namespace MediaTray.Tests;

public class SmtcMathTests
{
    private static readonly DateTimeOffset Updated = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Playing_adds_elapsed_time()
    {
        var result = SmtcMath.CurrentPosition(TimeSpan.FromSeconds(10), Updated, Updated.AddSeconds(5), true, 100);
        Assert.Equal(15, result, 3);
    }

    [Fact]
    public void Paused_keeps_reported_position()
    {
        var result = SmtcMath.CurrentPosition(TimeSpan.FromSeconds(10), Updated, Updated.AddSeconds(5), false, 100);
        Assert.Equal(10, result, 3);
    }

    [Fact]
    public void Clamped_to_duration()
    {
        var result = SmtcMath.CurrentPosition(TimeSpan.FromSeconds(98), Updated, Updated.AddSeconds(30), true, 100);
        Assert.Equal(100, result, 3);
    }

    [Fact]
    public void Unknown_duration_is_not_clamped_above_but_never_negative()
    {
        Assert.Equal(40, SmtcMath.CurrentPosition(TimeSpan.FromSeconds(10), Updated, Updated.AddSeconds(30), true, null), 3);
        Assert.Equal(0, SmtcMath.CurrentPosition(TimeSpan.FromSeconds(1), Updated.AddSeconds(5), Updated, true, null), 3);
    }
}
