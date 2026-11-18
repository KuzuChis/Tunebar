using Tunebar.Core;

namespace Tunebar.Tests;

public class FormatTests
{
    [Fact] public void Time_formats_minutes() => Assert.Equal("1:24", Format.Time(84.2));
    [Fact] public void Time_formats_hours() => Assert.Equal("1:02:05", Format.Time(3725));
    [Fact] public void Time_null_is_placeholder() => Assert.Equal("--:--", Format.Time(null));
    [Fact] public void Time_zero() => Assert.Equal("0:00", Format.Time(0));

    [Fact]
    public void PlaylistHeader_is_one_based()
    {
        var items = Enumerable.Range(1, 12).Select(i => new PlaylistItem($"Lagu {i}", null)).ToList();
        Assert.Equal("Playlist · 3 dari 12", Format.PlaylistHeader(new Playlist(2, items)));
    }

    [Fact] public void OthersLabel_all_playing() => Assert.Equal("2 sumber lain memutar", Format.OthersLabel(2, 2));
    [Fact] public void OthersLabel_some_playing() => Assert.Equal("3 sumber lain · 1 memutar", Format.OthersLabel(3, 1));
    [Fact] public void OthersLabel_none_playing() => Assert.Equal("2 sumber lain", Format.OthersLabel(2, 0));
    [Fact] public void Percent_normal() => Assert.Equal("70%", Format.Percent(0.7));
    [Fact] public void Percent_boosted() => Assert.Equal("150%", Format.Percent(1.5));
}
