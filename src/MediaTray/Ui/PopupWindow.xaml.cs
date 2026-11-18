using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using MediaTray.Core;

namespace MediaTray.Ui;

/// <summary>
/// The flyout shown above the tray. Binding handles most things; this file holds what XAML cannot:
/// window position, rounded corners, hide-on-deactivate, and the slider drag rules.
/// </summary>
public partial class PopupWindow : Window
{
    private const double ScreenMargin = 12;
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan VolumeInterval = TimeSpan.FromMilliseconds(100);

    private readonly PopupViewModel _viewModel;
    private readonly DispatcherTimer _volumeTimer;
    private readonly Stopwatch _sinceVolumeSent = Stopwatch.StartNew();

    private bool _syncingSliders;   // true while we move a slider from code (do not send commands then)
    private bool _seekDragging;
    private bool _volumeDragging;
    private long _lastDeactivateTick;

    public PopupWindow(PopupViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        _volumeTimer = new DispatcherTimer { Interval = VolumeInterval };
        _volumeTimer.Tick += (_, _) => SendVolume();

        SourceInitialized += (_, _) => RoundCorners();
        Deactivated += OnDeactivated;
        SizeChanged += (_, _) => { if (IsVisible) PositionBottomRight(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };
        viewModel.PropertyChanged += OnViewModelChanged;

        SyncSliders();
    }

    // ---- Showing and hiding ----

    /// <summary>Shows the popup, or hides it when it is already open.</summary>
    public void Toggle()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        // Clicking the tray icon first deactivates (and hides) us. Without this guard
        // the same click would immediately open the popup again.
        if (Stopwatch.GetElapsedTime(_lastDeactivateTick) < ReopenGuard) return;

        PositionBottomRight();
        Show();
        UpdateLayout();
        PositionBottomRight();
        Activate();
        SyncSliders();
        ScrollToCurrentSong();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        _lastDeactivateTick = Stopwatch.GetTimestamp();
        Hide();
    }

    private void PositionBottomRight()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - ScreenMargin;
        Top = area.Bottom - ActualHeight - ScreenMargin;
    }

    // ---- Rounded corners (Windows 11) ----

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void RoundCorners()
    {
        const int WindowCornerPreference = 33;
        const int Round = 2;
        var preference = Round;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, WindowCornerPreference, ref preference, sizeof(int));
    }

    // ---- Keeping the view up to date ----

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PopupViewModel.Current):
                SyncSliders();
                break;
            case nameof(PopupViewModel.PlaylistRows):
                ScrollToCurrentSong();
                break;
        }
    }

    /// <summary>Moves both sliders to the values of the current source, unless the user is dragging.</summary>
    private void SyncSliders()
    {
        var source = _viewModel.Current;
        _syncingSliders = true;
        try
        {
            if (source?.DurationSec is { } duration)
            {
                SeekSlider.Maximum = Math.Max(duration, 1);
                if (!_seekDragging) SeekSlider.Value = Math.Clamp(source.PositionSec ?? 0, 0, SeekSlider.Maximum);
            }

            if (source?.Volume is { } volume)
            {
                VolumeSlider.Maximum = source.MaxVolume;
                if (!_volumeDragging) VolumeSlider.Value = Math.Clamp(volume, 0, source.MaxVolume);
                VolumeText.Text = Format.Percent(VolumeSlider.Value);
            }
        }
        finally
        {
            _syncingSliders = false;
        }
    }

    /// <summary>Scrolls the playlist so the playing song is visible (after the list has been laid out).</summary>
    private void ScrollToCurrentSong()
    {
        var row = _viewModel.PlaylistRows.FirstOrDefault(r => r.IsCurrent);
        if (row is null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => PlaylistList.ScrollIntoView(row));
    }

    // ---- Seek slider: send once, when the drag ends ----

    private void OnSeekDragStarted(object sender, DragStartedEventArgs e) => _seekDragging = true;

    private void OnSeekDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _seekDragging = false;
        UiTasks.Fire(() => _viewModel.SeekAsync(SeekSlider.Value));
    }

    private void OnSeekValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Mouse changes are sent when the drag completes; this covers the keyboard.
        if (_syncingSliders || _seekDragging) return;
        UiTasks.Fire(() => _viewModel.SeekAsync(e.NewValue));
    }

    // ---- Volume slider: send at most every 100 ms while dragging, and once at the end ----

    private void OnVolumeDragStarted(object sender, DragStartedEventArgs e) => _volumeDragging = true;

    private void OnVolumeDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _volumeDragging = false;
        _volumeTimer.Stop();
        SendVolume();
    }

    private void OnVolumeValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        VolumeText.Text = Format.Percent(e.NewValue);
        if (_syncingSliders) return;

        if (!_volumeDragging)
        {
            SendVolume(); // keyboard
            return;
        }

        if (_sinceVolumeSent.Elapsed >= VolumeInterval) SendVolume();
        else if (!_volumeTimer.IsEnabled) _volumeTimer.Start(); // send the latest value when the 100 ms are over
    }

    private void SendVolume()
    {
        _volumeTimer.Stop();
        _sinceVolumeSent.Restart();
        var value = VolumeSlider.Value;
        UiTasks.Fire(() => _viewModel.SetVolumeAsync(value));
    }

    // ---- Buttons ----

    private void OnLockClick(object sender, RoutedEventArgs e) => _viewModel.ToggleLock();

    private void OnPrevClick(object sender, RoutedEventArgs e) => UiTasks.Fire(_viewModel.PrevAsync);

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => UiTasks.Fire(_viewModel.TogglePlayAsync);

    private void OnNextClick(object sender, RoutedEventArgs e) => UiTasks.Fire(_viewModel.NextAsync);

    private void OnLoopClick(object sender, RoutedEventArgs e) => UiTasks.Fire(_viewModel.CycleLoopAsync);

    private void OnOthersToggleClick(object sender, RoutedEventArgs e) => _viewModel.ToggleOthers();

    private void OnPlaylistRowClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is Button { DataContext: PlaylistRow row })
            UiTasks.Fire(() => _viewModel.JumpAsync(row.Index));
    }

    private void OnOtherRowClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button { DataContext: OtherSourceRow row } button) return;

        switch (button.Tag as string)
        {
            case "select":
                _viewModel.SelectSource(row.Id);
                break;
            case "play":
                UiTasks.Fire(() => _viewModel.TogglePlayOtherAsync(row.Id));
                break;
            case "lock":
                _viewModel.LockSource(row.Id);
                break;
        }
    }
}
