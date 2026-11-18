namespace MediaTray.Windows;

/// <summary>Where the widget sits inside the taskbar, in physical pixels (relative to the taskbar's client area).</summary>
public sealed record TaskbarSlot(bool Visible, int X, int Width, int Height);

/// <summary>The sizing and show/hide rules of the taskbar widget. Pure maths, no Win32.</summary>
public static class TaskbarLayout
{
    private const double MarginDip = 8;
    private const double GapDip = 8;
    private const double MaxWidthDip = 300;
    private const double MinWidthDip = 120;
    private const double FallbackWidthDip = 240;
    private const double FallbackMinTaskbarDip = 1000;

    /// <summary>
    /// The free space at the left end of the taskbar: up to 300 DIP wide, 8 DIP from the edge,
    /// and 8 DIP short of the Start button. Hidden when less than 120 DIP is left.
    /// </summary>
    /// <param name="startButtonLeftPx">Left edge of the Start button, relative to the taskbar; null when not found.</param>
    public static TaskbarSlot Compute(double taskbarWidthPx, double taskbarHeightPx, double? startButtonLeftPx, double scale)
    {
        var x = (int)Math.Round(MarginDip * scale);
        var height = (int)Math.Round(taskbarHeightPx);
        var hidden = new TaskbarSlot(false, x, 0, height);

        double widthPx;
        if (startButtonLeftPx is { } startLeft)
        {
            widthPx = Math.Min(MaxWidthDip * scale, startLeft - (MarginDip + GapDip) * scale);
            if (widthPx < MinWidthDip * scale) return hidden;
        }
        else
        {
            if (taskbarWidthPx < FallbackMinTaskbarDip * scale) return hidden;
            widthPx = FallbackWidthDip * scale;
        }

        return new TaskbarSlot(true, x, (int)Math.Round(widthPx), height);
    }

    /// <summary>
    /// True when the widget may be shown: the user wants it, icons are centered (TaskbarAl is not 0),
    /// the Widgets button is off (TaskbarDa is not 1) and the taskbar is at the bottom (edge 3, or unknown).
    /// </summary>
    public static bool ShouldShow(int? taskbarAl, int? taskbarDa, int? stuckRectsEdge, bool userEnabled) =>
        userEnabled
        && taskbarAl != 0
        && taskbarDa != 1
        && (stuckRectsEdge is null or 3);
}
