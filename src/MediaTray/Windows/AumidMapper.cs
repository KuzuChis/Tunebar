namespace MediaTray.Windows;

/// <summary>
/// Turns a Windows AppUserModelID (AUMID) into a friendly app name and a process name.
/// An AUMID looks like "Spotify.exe" or "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify".
/// </summary>
public static class AumidMapper
{
    /// <summary>One app we recognise: a piece of the AUMID, the name to show, and the process name.</summary>
    private sealed record KnownApp(string Contains, string AppName, string ProcessName, bool IsBrowser);

    // Order matters: the first entry whose text is found in the AUMID wins.
    private static readonly KnownApp[] KnownApps =
    [
        new("Opera", "Opera", "opera", true),
        new("Chrome", "Chrome", "chrome", true),
        new("MSEdge", "Edge", "msedge", true),
        new("Firefox", "Firefox", "firefox", true),
        new("Brave", "Brave", "brave", true),
        new("Spotify", "Spotify", "spotify", false),
    ];

    /// <summary>The name to show for this app, for example "Spotify".</summary>
    public static string AppName(string aumid)
    {
        var known = Find(aumid);
        if (known is not null) return known.AppName;

        // "Foo.Bar!App" -> "App"
        var bang = aumid.LastIndexOf('!');
        if (bang >= 0 && bang < aumid.Length - 1) return aumid[(bang + 1)..];

        return StripExe(aumid);
    }

    /// <summary>The process name used to find the app's audio sessions, or null when unknown.</summary>
    public static string? ProcessName(string aumid)
    {
        var known = Find(aumid);
        if (known is not null) return known.ProcessName;

        // A plain "vlc.exe" tells us the process name directly.
        if (aumid.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return StripExe(aumid);

        return null;
    }

    /// <summary>True for web browsers. Their volume is controlled per tab, not per app.</summary>
    public static bool IsBrowser(string aumid) => Find(aumid)?.IsBrowser ?? false;

    private static KnownApp? Find(string aumid) =>
        KnownApps.FirstOrDefault(app => aumid.Contains(app.Contains, StringComparison.OrdinalIgnoreCase));

    private static string StripExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
