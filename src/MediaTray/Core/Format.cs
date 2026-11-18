using System.Globalization;

namespace MediaTray.Core;

/// <summary>Turns numbers into the text shown in the tray UI.</summary>
public static class Format
{
    /// <summary>"1:24" or "1:02:05"; "--:--" when the time is unknown.</summary>
    public static string Time(double? seconds)
    {
        if (seconds is null || double.IsNaN(seconds.Value) || double.IsInfinity(seconds.Value))
            return "--:--";

        var total = (long)Math.Floor(Math.Max(0, seconds.Value));
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var secs = total % 60;

        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{secs:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{secs:00}");
    }

    /// <summary>"Playlist · 3 dari 12" (the index is zero-based, the text is one-based).</summary>
    public static string PlaylistHeader(Playlist p) =>
        string.Create(CultureInfo.InvariantCulture, $"Playlist · {p.Index + 1} dari {p.Items.Count}");

    /// <summary>
    /// "2 sumber lain memutar" when all are playing, "3 sumber lain · 1 memutar" when some are,
    /// "2 sumber lain" when none is.
    /// </summary>
    public static string OthersLabel(int count, int playingCount)
    {
        if (playingCount <= 0)
            return string.Create(CultureInfo.InvariantCulture, $"{count} sumber lain");
        if (playingCount == count)
            return string.Create(CultureInfo.InvariantCulture, $"{count} sumber lain memutar");
        return string.Create(CultureInfo.InvariantCulture, $"{count} sumber lain · {playingCount} memutar");
    }

    /// <summary>Volume 0.7 becomes "70%"; boosted volume 1.5 becomes "150%".</summary>
    public static string Percent(double volume) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Round(volume * 100)}%");
}
