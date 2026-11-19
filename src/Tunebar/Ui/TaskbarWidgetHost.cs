using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Tunebar.Core;
using Tunebar.Windows;

namespace Tunebar.Ui;

/// <summary>
/// Puts the <see cref="TaskbarWidget"/> inside the taskbar window (Shell_TrayWnd) as a child window
/// and keeps it there: every 2 seconds it checks the taskbar, then creates, moves, resizes or removes
/// the widget, and rebuilds it after explorer restarts. Problems never escape the timer; without the
/// widget the app still works through the tray icon.
/// </summary>
public sealed class TaskbarWidgetHost : IDisposable
{
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipSiblings = 0x04000000;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int GwlStyle = -16;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly PopupViewModel _viewModel;
    private readonly TaskbarWatcher _watcher = new();
    private readonly WakeGuard _wakeGuard = new();
    private readonly DispatcherTimer _timer;

    private HwndSource? _source;
    private TaskbarWidget? _widget;
    private IntPtr _tray;
    private TaskbarSlot? _slot;
    private int _trayLeftPx;
    private bool _enabled;
    private bool _busy;
    private bool _disposed;

    public TaskbarWidgetHost(PopupViewModel viewModel, bool enabled)
    {
        _viewModel = viewModel;
        _enabled = enabled;
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += (_, _) => Update();
        _timer.Start();
        Update();
    }

    /// <summary>Raised when the user clicks the cover/title area of the widget.</summary>
    public event EventHandler? OpenPopupRequested;

    /// <summary>Whether the user wants the widget. Turning it off removes it right away.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (value) Update();
            else Destroy();
        }
    }

    /// <summary>The widget's left edge on the screen in DIP (to line the popup up with it); null when there is no widget.</summary>
    public double? WidgetLeftDip
    {
        get
        {
            if (_source?.CompositionTarget is not { } target || _slot is null) return null;
            return (_trayLeftPx + _slot.X) * target.TransformFromDevice.M11;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        Destroy();
    }

    // ---- The timer ----

    private async void Update()
    {
        // The guard sees every tick, even busy ones, so a slow taskbar read is not mistaken for a sleep gap.
        // Just woke from sleep: explorer is still starting up, so leave the taskbar alone for a bit.
        var wakeSkip = _wakeGuard.ShouldSkip(TimeSpan.FromMilliseconds(Environment.TickCount64));
        if (_busy || _disposed || wakeSkip) return;
        _busy = true;
        try
        {
            // The taskbar is read on a background thread (UI Automation); we continue here on the UI thread.
            var state = await _watcher.ReadAsync();
            if (!_disposed) Apply(state);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Taskbar widget update failed: {ex}");
            Destroy(); // start clean on the next tick
        }
        finally
        {
            _busy = false;
        }
    }

    private void Apply(TaskbarState? state)
    {
        if (!_enabled || state is null
            || !TaskbarLayout.ShouldShow(state.TaskbarAl, state.TaskbarDa, state.Edge, userEnabled: true))
        {
            Destroy();
            return;
        }

        var slot = TaskbarLayout.Compute(state.Width, state.Height, state.StartLeftPx, state.Scale);
        if (!slot.Visible)
        {
            Destroy();
            return;
        }

        // Explorer restarted: the old child went away with the old taskbar window.
        if (_source is not null && (_tray != state.Handle || !IsWindow(_tray) || !IsWindow(_source.Handle)))
            Destroy();

        if (_source is null)
        {
            Create(state, slot);
            return;
        }

        var hwnd = _source.Handle;
        _trayLeftPx = state.Left;
        // Also repair a widget someone else moved or resized, not only a changed slot.
        if (slot != _slot || !IsAtSlot(hwnd, slot))
        {
            SetWindowPos(hwnd, IntPtr.Zero, slot.X, slot.Y, slot.Width, slot.Height, SwpNoActivate);
            _slot = slot;
        }
        KeepOnTop(hwnd);
    }

    // ---- The child window ----

    private void Create(TaskbarState state, TaskbarSlot slot)
    {
        HwndSource? source = null;
        try
        {
            // Created as a popup first: creating it directly as a child of the taskbar fails because
            // this process and explorer have different DPI awareness. It is re-parented right after.
            source = new HwndSource(new HwndSourceParameters("TunebarWidget")
            {
                WindowStyle = WsPopup,
                ExtendedWindowStyle = WsExNoActivate | WsExToolWindow,
                Width = slot.Width,
                Height = slot.Height,
                UsesPerPixelTransparency = true,
                HwndSourceHook = WndProc,
            });

            var widget = new TaskbarWidget(_viewModel);
            widget.OpenPopupRequested += OnWidgetOpenRequested;
            source.RootVisual = widget;

            var hwnd = source.Handle;
            SetWindowLongPtr(hwnd, GwlStyle, (IntPtr)(WsChild | WsVisible | WsClipSiblings));
            if (SetParent(hwnd, state.Handle) == IntPtr.Zero)
                throw new InvalidOperationException($"SetParent failed ({Marshal.GetLastWin32Error()})");
            SetWindowPos(hwnd, IntPtr.Zero, slot.X, slot.Y, slot.Width, slot.Height, SwpNoActivate | SwpFrameChanged);

            _source = source;
            _widget = widget;
            _tray = state.Handle;
            _slot = slot;
            _trayLeftPx = state.Left;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Creating the taskbar widget failed: {ex.Message}");
            try { source?.Dispose(); } catch (Exception) { /* nothing left to clean */ }
        }
    }

    private void Destroy()
    {
        var source = _source;
        var widget = _widget;
        _source = null;
        _widget = null;
        _slot = null;
        _tray = IntPtr.Zero;

        if (widget is not null) widget.OpenPopupRequested -= OnWidgetOpenRequested;
        try
        {
            if (source is not null)
            {
                source.RootVisual = null;
                source.Dispose();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Removing the taskbar widget failed: {ex.Message}");
        }
    }

    private void OnWidgetOpenRequested(object? sender, EventArgs e) => OpenPopupRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// A click on the widget must not make the taskbar the foreground window, or an auto-hide taskbar
    /// stays stuck open. So the widget never takes activation.
    /// </summary>
    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmMouseActivate) return IntPtr.Zero;
        handled = true;
        return (IntPtr)MaNoActivate;
    }

    /// <summary>True when the widget's real rect, in taskbar client coordinates, equals the slot.</summary>
    private bool IsAtSlot(IntPtr hwnd, TaskbarSlot slot)
    {
        if (!GetWindowRect(hwnd, out var rect)) return false;
        var points = new[] { new Point { X = rect.Left, Y = rect.Top }, new Point { X = rect.Right, Y = rect.Bottom } };
        if (MapWindowPoints(IntPtr.Zero, _tray, points, 2) == 0 && Marshal.GetLastWin32Error() != 0) return false;
        return points[0].X == slot.X && points[0].Y == slot.Y
            && points[1].X - points[0].X == slot.Width && points[1].Y - points[0].Y == slot.Height;
    }

    /// <summary>Taskbar buttons can cover us after they change; put the widget back on top.</summary>
    private static void KeepOnTop(IntPtr hwnd) =>
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int MapWindowPoints(IntPtr from, IntPtr to, [In, Out] Point[] points, uint count);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
