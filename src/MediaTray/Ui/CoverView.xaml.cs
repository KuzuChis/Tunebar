using System.Windows;
using System.Windows.Controls;
using MediaTray.Core;

namespace MediaTray.Ui;

public partial class CoverView : UserControl
{
    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(nameof(Item), typeof(MediaSource), typeof(CoverView));

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(CoverView),
            new PropertyMetadata(new CornerRadius(6)));

    public static readonly DependencyProperty GlyphSizeProperty =
        DependencyProperty.Register(nameof(GlyphSize), typeof(double), typeof(CoverView),
            new PropertyMetadata(24.0));

    public CoverView() => InitializeComponent();

    /// <summary>The source whose cover is shown.</summary>
    public MediaSource? Item
    {
        get => (MediaSource?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>Size of the music note shown when there is no cover.</summary>
    public double GlyphSize
    {
        get => (double)GetValue(GlyphSizeProperty);
        set => SetValue(GlyphSizeProperty, value);
    }
}
