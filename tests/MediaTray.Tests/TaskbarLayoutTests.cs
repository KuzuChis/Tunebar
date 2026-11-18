using MediaTray.Windows;

namespace MediaTray.Tests;

public class TaskbarLayoutTests
{
    [Fact]
    public void Plenty_of_room_gives_the_full_300_dip()
    {
        var slot = TaskbarLayout.Compute(1920, 72, 800, 1.5);
        Assert.True(slot.Visible);
        Assert.Equal(12, slot.X);
        Assert.Equal(450, slot.Width);
        Assert.Equal(72, slot.Height);
    }

    [Fact]
    public void Narrow_room_shrinks_the_widget()
    {
        // Start at 400 px: 400 - 8*1.5 - 8*1.5 = 376 px at scale 1.5.
        var slot = TaskbarLayout.Compute(1920, 72, 400, 1.5);
        Assert.True(slot.Visible);
        Assert.Equal(376, slot.Width);
    }

    [Fact]
    public void Less_than_120_dip_hides_it()
    {
        // 120 DIP = 180 px; start at 200 leaves 200 - 24 = 176 px.
        Assert.False(TaskbarLayout.Compute(1920, 72, 200, 1.5).Visible);
        // Exactly 180 px is still shown.
        Assert.True(TaskbarLayout.Compute(1920, 72, 204, 1.5).Visible);
    }

    [Fact]
    public void Scale_one_uses_dip_as_pixels()
    {
        var slot = TaskbarLayout.Compute(1920, 48, 900, 1.0);
        Assert.Equal(8, slot.X);
        Assert.Equal(300, slot.Width);
        Assert.Equal(48, slot.Height);
    }

    [Fact]
    public void Unknown_start_button_uses_240_dip_on_a_wide_taskbar()
    {
        var slot = TaskbarLayout.Compute(1920, 72, null, 1.5);
        Assert.True(slot.Visible);
        Assert.Equal(360, slot.Width);
    }

    [Fact]
    public void Unknown_start_button_on_a_narrow_taskbar_hides_it() =>
        Assert.False(TaskbarLayout.Compute(1400, 72, null, 1.5).Visible); // 933 DIP

    [Theory]
    [InlineData(null, null, null, true, true)]
    [InlineData(1, 0, 3, true, true)]
    [InlineData(0, null, null, true, false)]   // icons aligned left
    [InlineData(null, 1, null, true, false)]   // Widgets button on
    [InlineData(null, null, 0, true, false)]   // taskbar on the left
    [InlineData(null, null, 1, true, false)]   // top
    [InlineData(null, null, 2, true, false)]   // right
    [InlineData(null, null, 3, false, false)]  // user turned it off
    public void ShouldShow_follows_the_rules(int? al, int? da, int? edge, bool enabled, bool expected) =>
        Assert.Equal(expected, TaskbarLayout.ShouldShow(al, da, edge, enabled));
}
