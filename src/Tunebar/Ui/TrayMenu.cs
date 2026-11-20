using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Tunebar.Ui;

/// <summary>One line of the tray menu. A null text is a separator.</summary>
public sealed record TrayMenuItem(string? Text, Action? OnClick = null, bool Enabled = true, bool Checked = false)
{
    public static TrayMenuItem Separator { get; } = new(Text: null);
}

/// <summary>
/// The tray icon's right-click menu as a native Windows menu. A WPF ContextMenu closes itself at once when the
/// icon sits in the "Show hidden icons" panel: that panel takes the foreground back right after the menu opens.
/// A native menu (TrackPopupMenuEx with the usual SetForegroundWindow / WM_NULL steps) is what Windows expects
/// there and stays open.
/// </summary>
public sealed class TrayMenu : IDisposable
{
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExToolWindow = 0x00000080;
    private const uint MfString = 0x0000;
    private const uint MfGrayed = 0x0001;
    private const uint MfChecked = 0x0008;
    private const uint MfSeparator = 0x0800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmBottomAlign = 0x0020;
    private const uint TpmReturnCmd = 0x0100;
    private const uint WmNull = 0x0000;

    // An invisible window of ours: the menu needs an owner that can become the foreground window.
    private readonly HwndSource _owner = new(new HwndSourceParameters("TunebarTrayMenu")
    {
        WindowStyle = WsPopup,
        ExtendedWindowStyle = WsExToolWindow,
        Width = 0,
        Height = 0,
    });

    /// <summary>Shows the items at the mouse position and runs the chosen one. Blocks until the menu closes.</summary>
    public void Show(IReadOnlyList<TrayMenuItem> items)
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Text is null)
                {
                    AppendMenu(menu, MfSeparator, UIntPtr.Zero, null);
                    continue;
                }
                var flags = MfString | (item.Enabled ? 0 : MfGrayed) | (item.Checked ? MfChecked : 0);
                AppendMenu(menu, flags, (UIntPtr)(uint)(i + 1), item.Text); // ids start at 1: 0 means "nothing chosen"
            }

            GetCursorPos(out var at);
            var hwnd = _owner.Handle;
            SetForegroundWindow(hwnd); // without this the menu would not close when clicking elsewhere
            var chosen = TrackPopupMenuEx(menu, TpmReturnCmd | TpmRightButton | TpmBottomAlign, at.X, at.Y, hwnd, IntPtr.Zero);
            PostMessage(hwnd, WmNull, IntPtr.Zero, IntPtr.Zero); // documented companion to SetForegroundWindow above

            if (chosen > 0 && chosen <= items.Count) items[chosen - 1].OnClick?.Invoke();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose() => _owner.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
