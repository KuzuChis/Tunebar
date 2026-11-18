using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Microsoft.Win32;

namespace Tunebar.Windows;

/// <summary>What the taskbar looks like right now. Rectangle and Start position are in physical pixels.</summary>
/// <param name="StartLeftPx">Left edge of the Start button relative to the taskbar; null when not found.</param>
public sealed record TaskbarState(
    IntPtr Handle, int Left, int Top, int Width, int Height, double Scale,
    int? TaskbarAl, int? TaskbarDa, int? Edge, double? StartLeftPx);

/// <summary>
/// Looks at the taskbar: its window, its settings in the registry and where the Start button is.
/// Everything here is read-only. The Start button is found with UI Automation, which walks the taskbar
/// (and our own widget inside it), so that part must never run on the UI thread.
/// </summary>
public sealed class TaskbarWatcher
{
    private const string TrayClass = "Shell_TrayWnd";
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string StuckRectsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3";

    private AutomationElement? _startButton;
    private IntPtr _startButtonTray;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    /// <summary>The current taskbar window, or zero when there is none.</summary>
    public static IntPtr FindTaskbar() => FindWindow(TrayClass, null);

    /// <summary>
    /// The taskbar height in DIP of this (system-DPI-aware) process, for placing the popup above it.
    /// Falls back to 48.
    /// </summary>
    public static double TaskbarHeightDip()
    {
        try
        {
            var tray = FindTaskbar();
            if (tray == IntPtr.Zero || !GetWindowRect(tray, out var rect)) return 48;
            var height = (rect.Bottom - rect.Top) * 96.0 / Math.Max(GetDpiForSystem(), 96u);
            return height > 0 ? height : 48;
        }
        catch (Exception)
        {
            return 48;
        }
    }

    /// <summary>
    /// Reads the taskbar on a background thread and returns null when it cannot be found.
    /// The result is handed back to the caller's context (the UI thread), not computed there.
    /// </summary>
    public Task<TaskbarState?> ReadAsync() => Task.Run(Read);

    private TaskbarState? Read()
    {
        try
        {
            var tray = FindTaskbar();
            if (tray == IntPtr.Zero || !GetWindowRect(tray, out var rect)) return null;

            var dpi = GetDpiForWindow(tray);
            var scale = (dpi == 0 ? 96 : dpi) / 96.0;
            var (al, da) = ReadAdvanced();
            var edge = ReadEdge();

            double? startLeft = null;
            if (FindStartLeftScreen(tray) is { } screenLeft) startLeft = screenLeft - rect.Left;

            return new TaskbarState(tray, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top,
                scale, al, da, edge, startLeft);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Reading the taskbar failed: {ex.Message}");
            return null;
        }
    }

    private static (int? Al, int? Da) ReadAdvanced()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey);
            return (key?.GetValue("TaskbarAl") as int?, key?.GetValue("TaskbarDa") as int?);
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    /// <summary>Taskbar edge from StuckRects3: 0 left, 1 top, 2 right, 3 bottom.</summary>
    private static int? ReadEdge()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StuckRectsKey);
            return key?.GetValue("Settings") is byte[] { Length: > 12 } settings ? settings[12] : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Screen-left of the Start button. The element is cached; it is looked up again when reading it fails.</summary>
    private double? FindStartLeftScreen(IntPtr tray)
    {
        if (_startButton is not null && _startButtonTray == tray)
        {
            try
            {
                var cached = _startButton.Current.BoundingRectangle;
                if (!cached.IsEmpty) return cached.Left;
            }
            catch (Exception)
            {
                // The element went stale (explorer rebuilt it); search again below.
            }
        }

        _startButton = null;
        try
        {
            var root = AutomationElement.FromHandle(tray);
            var found = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "StartButton"));
            if (found is null) return null;

            var rect = found.Current.BoundingRectangle;
            if (rect.IsEmpty) return null;

            _startButton = found;
            _startButtonTray = tray;
            return rect.Left;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Finding the Start button failed: {ex.Message}");
            return null;
        }
    }
}
