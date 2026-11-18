using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MediaTray.Ui;

/// <summary>
/// The small control embedded in the taskbar: cover, title/artist and the three transport buttons.
/// Its colors follow the taskbar theme, not the app theme.
/// </summary>
public partial class TaskbarWidget : UserControl
{
    private readonly PopupViewModel _viewModel;

    public TaskbarWidget(PopupViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        ApplyTaskbarTheme();
    }

    /// <summary>Raised when the cover/title area is clicked (the popup should open above the widget).</summary>
    public event EventHandler? OpenPopupRequested;

    /// <summary>Loads the light or dark colors that match the taskbar, so the DynamicResource lookups resolve here.</summary>
    private void ApplyTaskbarTheme()
    {
        var light = Theme.TaskbarIsLight();
        var colors = light ? "Theme.Light.xaml" : "Theme.Dark.xaml";
        Resources.MergedDictionaries.Add(
            new ResourceDictionary { Source = new Uri($"/MediaTray;component/Ui/{colors}", UriKind.Relative) });

        var hover = new SolidColorBrush(light ? Color.FromArgb(0x19, 0, 0, 0) : Color.FromArgb(0x19, 0xFF, 0xFF, 0xFF));
        hover.Freeze();
        Resources["WidgetHoverBrush"] = hover;
    }

    private void OnOpenClick(object sender, RoutedEventArgs e) => OpenPopupRequested?.Invoke(this, EventArgs.Empty);

    private void OnPrevClick(object sender, RoutedEventArgs e) => UiTasks.Fire(_viewModel.PrevAsync);

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => UiTasks.Fire(_viewModel.TogglePlayAsync);

    private void OnNextClick(object sender, RoutedEventArgs e) => UiTasks.Fire(_viewModel.NextAsync);
}
