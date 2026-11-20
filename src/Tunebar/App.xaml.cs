using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using H.NotifyIcon;
using Tunebar.Bridge;
using Tunebar.Core;
using Tunebar.Ui;
using Tunebar.Windows;

namespace Tunebar;

/// <summary>
/// Composition root: creates every piece (registry, extension server, Windows media, popup, tray icon),
/// connects them, and tears them down on exit.
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceName = "Tunebar.SingleInstance";

    private Mutex? _singleInstance;
    private ExtensionServer? _server;
    private SmtcProvider? _smtc;
    private TaskbarIcon? _tray;
    private TrayMenu? _trayMenu;
    private PopupWindow? _popup;
    private AutoStart? _autoStart;
    private WidgetSettings? _widgetSettings;
    private TaskbarWidgetHost? _widgetHost;
    private GlobalHotkeys? _hotkeys;

    private const double VolumeStep = 0.1;

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimum, IntPtr maximum);

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Safety net first: a stray exception in a tray app should be logged, not kill the icon.
        DispatcherUnhandledException += (_, args) =>
        {
            Debug.WriteLine($"Unhandled UI exception: {args.Exception}");
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Debug.WriteLine($"Unobserved task exception: {args.Exception}");
            args.SetObserved();
        };

        base.OnStartup(e);

        // Only one Tunebar at a time. A second copy exits quietly.
        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        // Software rendering uses less memory than the GPU pipeline for a small popup.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        Theme.Apply(this);

        _autoStart = CreateAutoStart();
        try
        {
            LegacyMigration.CreateDefault(_autoStart).Run();
            _autoStart.ApplyFirstRunDefault();
            _autoStart.Reconcile();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Auto-start setup failed: {ex.Message}");
        }

        var registry = new SourceRegistry(() => DateTimeOffset.Now);

        // Extension (browser tabs). Its events come from background threads, so hop to the UI thread first.
        _server = new ExtensionServer(ExtensionIdentity.Port, new OriginValidator(ExtensionIdentity.Id));
        _server.TabsReceived += tabs => Dispatcher.InvokeAsync(() => registry.SetExtensionSources(tabs));
        _server.Disconnected += () => Dispatcher.InvokeAsync(registry.ClearExtensionSources);
        var portInUse = !TryStartServer(_server);

        // The tray icon and popup come first; Windows media is connected afterwards through LateCommands.
        var smtcCommands = new LateCommands();
        var commands = new CommandRouter(_server, smtcCommands);
        var viewModel = new PopupViewModel(registry, commands, action => Dispatcher.InvokeAsync(action));
        _popup = new PopupWindow(viewModel);
        _popup.IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is false) TrimWorkingSet();
        };

        _widgetSettings = new WidgetSettings(WidgetSettings.DefaultPath);
        var widgetHost = new TaskbarWidgetHost(viewModel, _widgetSettings.ShowInTaskbar);
        widgetHost.OpenPopupRequested += (_, _) => _popup?.Toggle(widgetHost.WidgetLeftDip);
        _widgetHost = widgetHost;

        _tray = CreateTrayIcon();
        if (portInUse)
            _tray.ShowNotification("Tunebar", $"Port {ExtensionIdentity.Port} dipakai program lain");

        // Global hotkeys control whatever source is shown (the locked one when locked).
        _hotkeys = new GlobalHotkeys(action => HandleHotkey(viewModel, action));
        if (_hotkeys.Failed.Count > 0)
            _tray.ShowNotification("Tunebar", "Hotkey tidak bisa diaktifkan");

        // Developer aid: "Tunebar.exe --show" opens the popup right away (for screenshots).
        if (e.Args.Contains("--show"))
            _popup.Toggle();

        // Windows media sessions (Spotify, VLC, ...). If this fails we still run, just without them.
        var smtc = await TryCreateSmtcAsync();
        if (smtc is null) return;
        if (_singleInstance is null) // the app was shut down while we were waiting
        {
            smtc.Dispose();
            return;
        }
        _smtc = smtc;
        smtcCommands.Target = smtc;
        smtc.SourcesChanged += sources => Dispatcher.InvokeAsync(() => registry.SetSmtcSources(sources));
    }

    private static void HandleHotkey(PopupViewModel viewModel, HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.PlayPause: UiTasks.Fire(viewModel.TogglePlayAsync); break;
            case HotkeyAction.Next: UiTasks.Fire(viewModel.NextAsync); break;
            case HotkeyAction.Prev: UiTasks.Fire(viewModel.PrevAsync); break;
            case HotkeyAction.VolumeUp: UiTasks.Fire(() => viewModel.StepVolumeAsync(VolumeStep)); break;
            case HotkeyAction.VolumeDown: UiTasks.Fire(() => viewModel.StepVolumeAsync(-VolumeStep)); break;
        }
    }

    /// <summary>Asks Windows to give back unused memory (the popup just hid, so the app is idle).</summary>
    private static void TrimWorkingSet()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            SetProcessWorkingSetSize(self.Handle, -1, -1);
        }
        catch (Exception)
        {
            // Only an optimisation.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }

    /// <summary>Starts the extension server. Returns false when the port is taken by another program.</summary>
    private static bool TryStartServer(ExtensionServer server)
    {
        try
        {
            server.Start();
            return true;
        }
        catch (Exception)
        {
            // The server stays unstarted: the app runs on, and commands to tabs simply go nowhere.
            return false;
        }
    }

    /// <summary>The production auto-start: this exe, a Task Scheduler task, settings in %LOCALAPPDATA%\Tunebar.</summary>
    private static AutoStart CreateAutoStart()
    {
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tunebar", "settings.json");
        return new AutoStart(new ScheduledTaskStartup(), settingsPath, Environment.ProcessPath ?? "");
    }

    private static async Task<SmtcProvider?> TryCreateSmtcAsync()
    {
        var volume = new AppVolumeController();
        try
        {
            return await SmtcProvider.CreateAsync(volume);
        }
        catch (Exception)
        {
            volume.Dispose();
            return null;
        }
    }

    private TaskbarIcon CreateTrayIcon()
    {
        // The icon is a music note drawn from Segoe Fluent Icons, in the color that suits the taskbar.
        var iconColor = Theme.TaskbarIsLight() ? Brushes.Black : Brushes.White;

        var tray = new TaskbarIcon
        {
            ToolTipText = "Tunebar",
            NoLeftClickDelay = true,
            IconSource = new GeneratedIconSource
            {
                Text = Glyphs.Music,
                FontFamily = new FontFamily(IconFontName()),
                Foreground = iconColor,
                Background = Brushes.Transparent,
            },
        };
        tray.TrayLeftMouseUp += (_, _) => _popup?.Toggle();
        tray.TrayRightMouseUp += (_, _) => ShowTrayMenu();
        tray.ForceCreate();
        return tray;
    }

    /// <summary>The right-click menu, built fresh each time so the check marks show the current state.</summary>
    private void ShowTrayMenu()
    {
        var showInTaskbar = _widgetSettings?.ShowInTaskbar ?? true;
        var autoStart = _autoStart?.IsEnabled ?? false;

        _trayMenu ??= new TrayMenu();
        _trayMenu.Show(
        [
            new TrayMenuItem("Hotkey: Ctrl+Alt+Shift + Spasi / ← / → / ↑ / ↓", Enabled: false),
            TrayMenuItem.Separator,
            new TrayMenuItem("Tampilkan di taskbar", () =>
            {
                if (_widgetHost is not null) _widgetHost.Enabled = !showInTaskbar;
                _widgetSettings?.Save(!showInTaskbar);
            }, Checked: showInTaskbar),
            new TrayMenuItem("Jalankan saat Windows mulai", () =>
            {
                try
                {
                    _autoStart?.SetEnabled(!autoStart);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Changing auto-start failed: {ex.Message}");
                }
            }, Checked: autoStart),
            TrayMenuItem.Separator,
            new TrayMenuItem("Keluar", ExitFromMenu),
        ]);
    }

    /// <summary>The tray icon generator takes exactly one font name, so pick the installed one.</summary>
    private static string IconFontName() =>
        Fonts.SystemFontFamilies.Any(f => f.Source == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    /// <summary>Exit from the tray menu.</summary>
    private void ExitFromMenu()
    {
        Cleanup();
        Shutdown();
    }

    private void Cleanup()
    {
        _hotkeys?.Dispose();
        _hotkeys = null;
        _widgetHost?.Dispose();
        _widgetHost = null;
        _tray?.Dispose();
        _tray = null;
        _trayMenu?.Dispose();
        _trayMenu = null;
        _server?.Dispose();
        _server = null;
        _smtc?.Dispose();
        _smtc = null;

        if (_singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
            _singleInstance = null;
        }
    }

    /// <summary>
    /// Forwards commands to <see cref="Target"/> once Windows media is ready.
    /// Until then (or if it never starts) every command does nothing.
    /// </summary>
    private sealed class LateCommands : ISourceCommands
    {
        public ISourceCommands? Target { get; set; }

        public Task TogglePlayAsync(MediaSource s) => Target?.TogglePlayAsync(s) ?? Task.CompletedTask;
        public Task NextAsync(MediaSource s) => Target?.NextAsync(s) ?? Task.CompletedTask;
        public Task PrevAsync(MediaSource s) => Target?.PrevAsync(s) ?? Task.CompletedTask;
        public Task SeekAsync(MediaSource s, double seconds) => Target?.SeekAsync(s, seconds) ?? Task.CompletedTask;
        public Task SetVolumeAsync(MediaSource s, double volume) => Target?.SetVolumeAsync(s, volume) ?? Task.CompletedTask;
        public Task SetLoopAsync(MediaSource s, LoopMode mode) => Target?.SetLoopAsync(s, mode) ?? Task.CompletedTask;
        public Task JumpAsync(MediaSource s, int index) => Target?.JumpAsync(s, index) ?? Task.CompletedTask;
    }
}
