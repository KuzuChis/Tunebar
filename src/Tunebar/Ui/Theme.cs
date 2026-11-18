using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Tunebar.Ui;

/// <summary>Reads the Windows colors (light/dark, accent) and loads the matching resource dictionaries.</summary>
public static class Theme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";
    private static readonly Color FallbackAccent = Color.FromRgb(0x00, 0x78, 0xD4);

    /// <summary>True when apps (like our popup) should use the light theme.</summary>
    public static bool AppsUseLightTheme() => ReadPersonalizeFlag("AppsUseLightTheme", defaultValue: false);

    /// <summary>True when the taskbar is light, so the tray icon must be dark to stay visible.</summary>
    public static bool TaskbarIsLight() => ReadPersonalizeFlag("SystemUsesLightTheme", defaultValue: false);

    /// <summary>The Windows accent color of the user; Windows blue when it cannot be read.</summary>
    public static Color AccentColor()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(DwmKey);
            if (key?.GetValue("AccentColor") is int abgr)
            {
                // Stored as 0xAABBGGRR.
                return Color.FromRgb((byte)(abgr & 0xFF), (byte)(abgr >> 8 & 0xFF), (byte)(abgr >> 16 & 0xFF));
            }
        }
        catch (Exception)
        {
            // Fall through to the default.
        }
        return FallbackAccent;
    }

    /// <summary>Adds the color dictionary, the accent brush and the shared styles to the application.</summary>
    public static void Apply(Application app)
    {
        var colors = AppsUseLightTheme() ? "Theme.Light.xaml" : "Theme.Dark.xaml";
        var accent = new SolidColorBrush(AccentColor());
        accent.Freeze();

        app.Resources["AccentBrush"] = accent;
        app.Resources.MergedDictionaries.Add(Load(colors));
        app.Resources.MergedDictionaries.Add(Load("Styles.xaml"));
    }

    private static ResourceDictionary Load(string file) =>
        new() { Source = new Uri($"/Tunebar;component/Ui/{file}", UriKind.Relative) };

    private static bool ReadPersonalizeFlag(string name, bool defaultValue)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            // 1 = light, 0 = dark.
            return key?.GetValue(name) is int value ? value != 0 : defaultValue;
        }
        catch (Exception)
        {
            return defaultValue;
        }
    }
}
