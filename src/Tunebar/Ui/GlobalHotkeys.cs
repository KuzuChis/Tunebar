using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Tunebar.Ui;

/// <summary>What a global hotkey asks the app to do.</summary>
public enum HotkeyAction { PlayPause, Next, Prev, VolumeUp, VolumeDown }

/// <summary>One hotkey: the key, whether holding it repeats, and how it is shown to the user.</summary>
public sealed record HotkeyBinding(HotkeyAction Action, uint VirtualKey, bool NoRepeat, string DisplayText);

/// <summary>The fixed table of hotkeys (all use Ctrl+Alt+Shift). Pure data, so it can be tested.</summary>
public static class HotkeyMap
{
    public const uint ModAlt = 0x1;
    public const uint ModControl = 0x2;
    public const uint ModShift = 0x4;
    public const uint ModNoRepeat = 0x4000;

    /// <summary>The modifiers shared by every hotkey.</summary>
    public const uint Modifiers = ModControl | ModAlt | ModShift;

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

    /// <summary>The modifier flags to pass to RegisterHotKey for a binding.</summary>
    public static uint FlagsFor(HotkeyBinding binding) => Modifiers | (binding.NoRepeat ? ModNoRepeat : 0);
}

/// <summary>
/// Registers the global hotkeys on a message-only window and reports each press through <see cref="Pressed"/>.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private const int WmHotkey = 0x0312;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private readonly Dictionary<int, HotkeyBinding> _byId = [];
    private readonly List<string> _failed = [];

    public GlobalHotkeys(Action<HotkeyAction> pressed)
    {
        Pressed = pressed;
        _source = new HwndSource(new HwndSourceParameters("TunebarHotkeys")
        {
            ParentWindow = HwndMessage,
            Width = 0,
            Height = 0,
        });
        _source.AddHook(WndProc);

        var id = 1;
        foreach (var binding in HotkeyMap.Bindings)
        {
            if (RegisterHotKey(_source.Handle, id, HotkeyMap.FlagsFor(binding), binding.VirtualKey))
                _byId[id] = binding;
            else
                _failed.Add(binding.DisplayText);
            id++;
        }
    }

    /// <summary>Called on the UI thread for each hotkey press.</summary>
    public Action<HotkeyAction> Pressed { get; }

    /// <summary>Display names of the combos another program already owns.</summary>
    public IReadOnlyList<string> Failed => _failed;

    public void Dispose()
    {
        foreach (var id in _byId.Keys) UnregisterHotKey(_source.Handle, id);
        _byId.Clear();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey) return IntPtr.Zero;
        try
        {
            if (_byId.TryGetValue(wParam.ToInt32(), out var binding))
            {
                handled = true;
                Pressed(binding.Action);
            }
        }
        catch (Exception ex)
        {
            // A broken handler must never take down the message loop.
            Debug.WriteLine($"Hotkey handler failed: {ex}");
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
