using Tunebar.Core;
using Tunebar.Windows;

namespace Tunebar.Tests;

public class SourceGraceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static MediaSource Src(string id, string title) => new(
        id, SourceKind.Smtc, "App", "App", title, null, null, null, true, 0, 100, true, true,
        null, 1.0, LoopMode.Off, [], null);

    private static Dictionary<string, MediaSource?> Built(string id, MediaSource? s) => new() { [id] = s };

    [Fact]
    public void Present_passes_through()
    {
        var grace = new SourceGrace();
        var s = Src("a", "Song");
        Assert.Same(s, Assert.Single(grace.Update(Built("a", s), T0)));
    }

    [Fact]
    public void Vanished_under_three_seconds_keeps_last_copy()
    {
        var grace = new SourceGrace();
        var s = Src("a", "Song");
        grace.Update(Built("a", s), T0);
        Assert.Same(s, Assert.Single(grace.Update(Built("a", null), T0.AddSeconds(2.9))));
    }

    [Fact]
    public void Not_reported_at_all_keeps_last_copy_then_drops()
    {
        var grace = new SourceGrace();
        var s = Src("a", "Song");
        grace.Update(Built("a", s), T0);
        Assert.Single(grace.Update(new Dictionary<string, MediaSource?>(), T0.AddSeconds(1)));
        Assert.Empty(grace.Update(new Dictionary<string, MediaSource?>(), T0.AddSeconds(3)));
    }

    [Fact]
    public void Vanished_three_seconds_or_more_is_dropped()
    {
        var grace = new SourceGrace();
        grace.Update(Built("a", Src("a", "Song")), T0);
        Assert.Empty(grace.Update(Built("a", null), T0.AddSeconds(3)));
    }

    [Fact]
    public void Empty_title_with_no_history_is_not_shown()
    {
        var grace = new SourceGrace();
        Assert.Empty(grace.Update(Built("a", Src("a", "  ")), T0));
    }

    [Fact]
    public void Empty_title_after_good_copy_keeps_the_good_copy()
    {
        var grace = new SourceGrace();
        var good = Src("a", "Song");
        grace.Update(Built("a", good), T0);
        Assert.Same(good, Assert.Single(grace.Update(Built("a", Src("a", "")), T0.AddSeconds(1))));
    }

    [Fact]
    public void Coming_back_uses_new_data()
    {
        var grace = new SourceGrace();
        grace.Update(Built("a", Src("a", "Old")), T0);
        grace.Update(Built("a", null), T0.AddSeconds(1));
        var fresh = Src("a", "New");
        Assert.Same(fresh, Assert.Single(grace.Update(Built("a", fresh), T0.AddSeconds(2))));
    }
}
