using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Tunebar.Ui;

/// <summary>What a global hotkey asks the app to do.</summary>
public enum HotkeyAction { PlayPause, Next, Prev, VolumeUp, VolumeDown }

/// <summary>One hotkey: the key, whether holding it repeats, and how it is shown to the user.</summary>
public sealed record HotkeyBinding(HotkeyAction Action, uint VirtualKey, bool NoRepeat, string DisplayText);

/// <summary>The fixed table of hotkeys (all use Ctrl+Alt+Shift). Pure data, so it can be tested.</summary>
public static class HotkeyMap
{
    public const uint VkSpace = 0x20;
    public const uint VkLeft = 0x25;
    public const uint VkUp = 0x26;
    public const uint VkRight = 0x27;
    public const uint VkDown = 0x28;

    public static IReadOnlyList<HotkeyBinding> Bindings { get; } =
    [
        new(HotkeyAction.PlayPause, VkSpace, true, "Ctrl+Alt+Shift+Spasi"),
        new(HotkeyAction.Next, VkRight, true, "Ctrl+Alt+Shift+→"),
        new(HotkeyAction.Prev, VkLeft, true, "Ctrl+Alt+Shift+←"),
        // Volume keys repeat on purpose: holding the key keeps stepping.
        new(HotkeyAction.VolumeUp, VkUp, false, "Ctrl+Alt+Shift+↑"),
        new(HotkeyAction.VolumeDown, VkDown, false, "Ctrl+Alt+Shift+↓"),
    ];
}

/// <summary>Which modifier keys are held, per side. Win is tracked only so it can veto a match.</summary>
public readonly record struct ModifierState(
    bool LCtrl, bool RCtrl, bool LAlt, bool RAlt, bool LShift, bool RShift, bool Win)
{
    /// <summary>True when Ctrl, Alt and Shift are each held on at least one side and Win is not held.</summary>
    public bool IsCtrlAltShift => (LCtrl || RCtrl) && (LAlt || RAlt) && (LShift || RShift) && !Win;
}

/// <summary>The outcome of a matched key press: the action, and whether to run it (false for a held-down repeat).</summary>
public readonly record struct HotkeyMatch(HotkeyAction Action, bool Fire);

/// <summary>Decides whether a key-down is one of our hotkeys. Pure, so it can be tested.</summary>
public static class HotkeyMatcher
{
    /// <summary>
    /// Returns the match for a key-down, or null if it is not ours. A match is always swallowed;
    /// <see cref="HotkeyMatch.Fire"/> is false for a repeat of a key that should not repeat.
    /// </summary>
    public static HotkeyMatch? Match(uint vk, bool alreadyDown, ModifierState mods)
    {
        if (!mods.IsCtrlAltShift) return null;
        foreach (var b in HotkeyMap.Bindings)
        {
            if (b.VirtualKey != vk) continue;
            return new HotkeyMatch(b.Action, !(b.NoRepeat && alreadyDown));
        }
        return null;
    }
}

/// <summary>
/// Watches the keyboard with a low-level hook on its own thread and reports each hotkey press.
/// A hook (not RegisterHotKey) lets us check modifiers per side, so tapping the other Shift key is harmless.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint WmQuit = 0x0012;

    private const int VkLShift = 0xA0, VkRShift = 0xA1, VkLCtrl = 0xA2, VkRCtrl = 0xA3;
    private const int VkLAlt = 0xA4, VkRAlt = 0xA5, VkLWin = 0x5B, VkRWin = 0x5C;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    private readonly Action<HotkeyAction> _pressed;
    private readonly Dispatcher _dispatcher;
    private readonly HookProc _proc; // field keeps the delegate alive for the hook's lifetime
    private readonly List<string> _failed = [];
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private uint _threadId;
    private bool _disposed;

    // Own tracking of the side-specific modifiers and the hotkey keys; only touched on the hook thread.
    private bool _lCtrl, _rCtrl, _lAlt, _rAlt, _lShift, _rShift, _lWin, _rWin;
    private readonly HashSet<uint> _down = [];

    public GlobalHotkeys(Action<HotkeyAction> pressed)
    {
        _pressed = pressed;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _proc = HookCallback;
        _thread = new Thread(Run) { IsBackground = true, Name = "TunebarHotkeys" };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>Names of what could not be enabled: "semua hotkey" if the keyboard hook failed, otherwise empty.</summary>
    public IReadOnlyList<string> Failed => _failed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_threadId != 0) PostThreadMessage(_threadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    private void Run()
    {
        // Touch the queue first so PostThreadMessage can reach this thread.
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        _threadId = GetCurrentThreadId();
        var hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) _failed.Add("semua hotkey");
        _ready.Set();
        if (hook == IntPtr.Zero) return;

        while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
        UnhookWindowsHookEx(hook);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                var msg = wParam.ToInt32();
                var isDown = msg is WmKeyDown or WmSysKeyDown;
                var isUp = msg is WmKeyUp or WmSysKeyUp;
                if (isDown || isUp)
                {
                    var vk = (uint)Marshal.ReadInt32(lParam);
                    TrackModifier((int)vk, isDown);
                    if (IsHotkeyKey(vk))
                    {
                        if (isUp)
                        {
                            _down.Remove(vk);
                        }
                        else
                        {
                            var already = !_down.Add(vk);
                            if (HotkeyMatcher.Match(vk, already, CurrentModifiers()) is { } m)
                            {
                                if (m.Fire)
                                {
                                    var action = m.Action;
                                    _dispatcher.BeginInvoke(() => Raise(action));
                                }
                                return (IntPtr)1;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Hotkey hook failed: {ex.Message}");
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void Raise(HotkeyAction action)
    {
        try { _pressed(action); }
        catch (Exception ex) { Debug.WriteLine($"Hotkey handler failed: {ex}"); }
    }

    private static bool IsHotkeyKey(uint vk) =>
        vk is HotkeyMap.VkSpace or HotkeyMap.VkLeft or HotkeyMap.VkRight or HotkeyMap.VkUp or HotkeyMap.VkDown;

    private void TrackModifier(int vk, bool down)
    {
        switch (vk)
        {
            case VkLCtrl: _lCtrl = down; break;
            case VkRCtrl: _rCtrl = down; break;
            case VkLAlt: _lAlt = down; break;
            case VkRAlt: _rAlt = down; break;
            case VkLShift: _lShift = down; break;
            case VkRShift: _rShift = down; break;
            case VkLWin: _lWin = down; break;
            case VkRWin: _rWin = down; break;
        }
    }

    /// <summary>A modifier counts as held if our own tracking or the OS async state says so.</summary>
    private ModifierState CurrentModifiers() => new(
        _lCtrl || IsAsyncDown(VkLCtrl), _rCtrl || IsAsyncDown(VkRCtrl),
        _lAlt || IsAsyncDown(VkLAlt), _rAlt || IsAsyncDown(VkRAlt),
        _lShift || IsAsyncDown(VkLShift), _rShift || IsAsyncDown(VkRShift),
        _lWin || _rWin || IsAsyncDown(VkLWin) || IsAsyncDown(VkRWin));

    private static bool IsAsyncDown(int vk) => GetAsyncKeyState(vk) < 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X, Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg msg, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out Msg msg, IntPtr hWnd, uint min, uint max, uint remove);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
