using System.Windows;
using MediaTray.Ui;

namespace MediaTray.Tests;

public class PopupPlacementTests
{
    private static readonly Rect Screen = new(0, 0, 1280, 768);

    [Fact]
    public void Normal_taskbar_puts_the_popup_bottom_right_of_the_work_area()
    {
        var work = new Rect(0, 0, 1280, 720); // taskbar of 48 DIP, always visible
        var p = PopupPlacement.Compute(work, Screen, 48, 340, 400, null);
        Assert.Equal(1280 - 340 - 12, p.X);
        Assert.Equal(720 - 12 - 400, p.Y);
    }

    [Fact]
    public void Auto_hide_keeps_the_popup_above_the_taskbar()
    {
        var p = PopupPlacement.Compute(Screen, Screen, 48, 340, 400, null); // work area reaches the screen bottom
        Assert.Equal(768 - 48 - 12 - 400, p.Y);
    }

    [Fact]
    public void Anchor_sets_the_left_edge()
    {
        var p = PopupPlacement.Compute(new Rect(0, 0, 1280, 720), Screen, 48, 340, 400, 50);
        Assert.Equal(50, p.X);
    }

    [Fact]
    public void Anchor_is_clamped_to_the_work_area_margin()
    {
        var p = PopupPlacement.Compute(new Rect(0, 0, 1280, 720), Screen, 48, 340, 400, 2);
        Assert.Equal(12, p.X);
    }
}
