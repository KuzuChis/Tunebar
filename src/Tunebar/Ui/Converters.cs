using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Tunebar.Core;

namespace Tunebar.Ui;

/// <summary>MediaSource to cover image (null when there is none).</summary>
public sealed class CoverConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        CoverImageCache.For(value as MediaSource);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true becomes Collapsed, false becomes Visible (the opposite of the built-in converter).</summary>
public sealed class InvertedBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
