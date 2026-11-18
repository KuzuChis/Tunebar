using System.Windows;

namespace MediaTray.Ui;

/// <summary>Where the popup goes. Pure maths, so it can be tested.</summary>
public static class PopupPlacement
{
    /// <summary>
    /// The popup sits just above the taskbar. With an auto-hide taskbar the work area reaches the bottom
    /// of the screen, so the taskbar height is subtracted from the screen instead.
    /// </summary>
    /// <param name="anchorLeft">Left edge to line up with (the widget); null means right-aligned in the work area.</param>
    public static Point Compute(Rect workArea, Rect screen, double taskbarHeightDip,
        double popupWidth, double popupHeight, double? anchorLeft, double margin = 12)
    {
        var bottom = Math.Min(workArea.Bottom, screen.Bottom - taskbarHeightDip) - margin;
        var left = anchorLeft is { } anchor
            ? Math.Max(workArea.Left + margin, anchor)
            : workArea.Right - popupWidth - margin;
        return new Point(left, bottom - popupHeight);
    }
}
