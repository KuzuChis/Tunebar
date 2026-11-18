using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using H.NotifyIcon;
using MediaTray.Bridge;
using MediaTray.Core;
using MediaTray.Ui;
using MediaTray.Windows;

namespace MediaTray;

/// <summary>
/// Composition root: creates every piece (registry, extension server, Windows media, popup, tray icon),
/// connects them, and tears them down on exit.
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceName = "MediaTray.SingleInstance";

    private Mutex? _singleInstance;
    private ExtensionServer? _server;
    private SmtcProvider? _smtc;
    private TaskbarIcon? _tray;
    private PopupWindow? _popup;
    private AutoStart? _autoStart;

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

        // Only one Media Tray at a time. A second copy exits quietly.
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

        _tray = CreateTrayIcon();
        if (portInUse)
            _tray.ShowNotification("Media Tray", $"Port {ExtensionIdentity.Port} dipakai program lain");

        // Developer aid: "MediaTray.exe --show" opens the popup right away (for screenshots).
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

    /// <summary>The production auto-start: this exe, the user's Run key, settings in %LOCALAPPDATA%\MediaTray.</summary>
    private static AutoStart CreateAutoStart()
    {
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaTray", "settings.json");
        return new AutoStart(AutoStart.DefaultRunKeyPath, settingsPath, Environment.ProcessPath ?? "");
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

        var menu = new ContextMenu();

        var autoStartItem = new MenuItem
        {
            Header = "Jalankan saat Windows mulai",
            IsCheckable = true,
            IsChecked = _autoStart?.IsEnabled ?? false,
        };
        autoStartItem.Click += (_, _) =>
        {
            if (_autoStart is null) return;
            try
            {
                _autoStart.SetEnabled(!_autoStart.IsEnabled);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Changing auto-start failed: {ex.Message}");
            }
            autoStartItem.IsChecked = _autoStart.IsEnabled;
        };
        menu.Items.Add(autoStartItem);
        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "Keluar" };
        exitItem.Click += (_, _) => ExitFromMenu();
        menu.Items.Add(exitItem);

        var tray = new TaskbarIcon
        {
            ToolTipText = "Media Tray",
            ContextMenu = menu,
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
        tray.ForceCreate();
        return tray;
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
        _tray?.Dispose();
        _tray = null;
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
