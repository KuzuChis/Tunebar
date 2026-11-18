using System.Windows;
using System.Windows.Controls;
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

    protected override async void OnStartup(StartupEventArgs e)
    {
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

        Theme.Apply(this);

        var registry = new SourceRegistry(() => DateTimeOffset.Now);

        // Extension (browser tabs). Its events come from background threads, so hop to the UI thread first.
        _server = new ExtensionServer(ExtensionIdentity.Port, new OriginValidator(ExtensionIdentity.Id));
        _server.TabsReceived += tabs => Dispatcher.InvokeAsync(() => registry.SetExtensionSources(tabs));
        _server.Disconnected += () => Dispatcher.InvokeAsync(registry.ClearExtensionSources);
        var portInUse = !TryStartServer(_server);

        // Windows media sessions (Spotify, VLC, ...). If this fails we still run, just without them.
        _smtc = await TryCreateSmtcAsync();
        if (_smtc is not null)
            _smtc.SourcesChanged += sources => Dispatcher.InvokeAsync(() => registry.SetSmtcSources(sources));

        var commands = new CommandRouter(_server, (ISourceCommands?)_smtc ?? new NoCommands());
        var viewModel = new PopupViewModel(registry, commands, action => Dispatcher.InvokeAsync(action));
        _popup = new PopupWindow(viewModel);

        _tray = CreateTrayIcon();
        if (portInUse)
            _tray.ShowNotification("Media Tray", $"Port {ExtensionIdentity.Port} dipakai program lain");

        // Developer aid: "MediaTray.exe --show" opens the popup right away (for screenshots).
        if (e.Args.Contains("--show"))
            _popup.Toggle();
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

    private static async Task<SmtcProvider?> TryCreateSmtcAsync()
    {
        try
        {
            return await SmtcProvider.CreateAsync(new AppVolumeController());
        }
        catch (Exception)
        {
            return null;
        }
    }

    private TaskbarIcon CreateTrayIcon()
    {
        // The icon is a music note drawn from Segoe Fluent Icons, in the color that suits the taskbar.
        var iconColor = Theme.TaskbarIsLight() ? Brushes.Black : Brushes.White;

        var menu = new ContextMenu();
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

    /// <summary>Used when Windows media could not start: every command does nothing.</summary>
    private sealed class NoCommands : ISourceCommands
    {
        public Task TogglePlayAsync(MediaSource s) => Task.CompletedTask;
        public Task NextAsync(MediaSource s) => Task.CompletedTask;
        public Task PrevAsync(MediaSource s) => Task.CompletedTask;
        public Task SeekAsync(MediaSource s, double seconds) => Task.CompletedTask;
        public Task SetVolumeAsync(MediaSource s, double volume) => Task.CompletedTask;
        public Task SetLoopAsync(MediaSource s, LoopMode mode) => Task.CompletedTask;
        public Task JumpAsync(MediaSource s, int index) => Task.CompletedTask;
    }
}
