using Tunebar.Core;

namespace Tunebar.Tests;

public class SourceRegistryTests
{
    private sealed class FakeClock
    {
        public DateTimeOffset Now { get; private set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public void Advance() => Now = Now.AddSeconds(1);
        public void Advance(TimeSpan by) => Now += by;
    }

    private static MediaSource Src(string id, SourceKind kind, bool playing, string title = "T", string app = "Opera") =>
        new(id, kind, app, app, title, null, null, null, playing, null, null,
            false, false, null, 1, LoopMode.Off, [], null);

    private static MediaSource Tab(int n, bool playing, string title = "T") =>
        Src($"tab:{n}", SourceKind.ExtensionTab, playing, title);

    private static MediaSource Smtc(string name, bool playing, string title = "T", string app = "Spotify") =>
        Src($"smtc:{name}", SourceKind.Smtc, playing, title, app);

    private static (SourceRegistry Registry, FakeClock Clock) Create()
    {
        var clock = new FakeClock();
        return (new SourceRegistry(() => clock.Now), clock);
    }

    [Fact]
    public void Current_is_most_recently_started_playing()
    {
        var (reg, clock) = Create();
        clock.Advance();
        reg.SetExtensionSources([Tab(1, true), Tab(2, false)]);
        clock.Advance();
        reg.SetExtensionSources([Tab(1, true), Tab(2, true)]);

        Assert.Equal("tab:2", reg.Current?.Id);
        Assert.Equal(["tab:1"], reg.Others.Select(o => o.Id));
    }

    [Fact]
    public void Falls_back_to_last_paused_when_none_playing()
    {
        var (reg, clock) = Create();
        clock.Advance();
        reg.SetExtensionSources([Tab(1, true), Tab(2, true)]);
        clock.Advance();
        reg.SetExtensionSources([Tab(1, true), Tab(2, false)]);   // B pauses at t=2
        clock.Advance();
        reg.SetExtensionSources([Tab(1, false), Tab(2, false)]);  // A pauses at t=3

        Assert.Equal("tab:1", reg.Current?.Id);
    }

    [Fact]
    public void Lock_keeps_current_when_other_starts()
    {
        var (reg, clock) = Create();
        clock.Advance();
        reg.SetExtensionSources([Tab(1, true), Tab(2, false)]);
        reg.Lock("tab:1");
        clock.Advance();
        reg.SetExtensionSources([Tab(1, true), Tab(2, true)]);

        Assert.Equal("tab:1", reg.Current?.Id);
        Assert.Equal("tab:1", reg.LockedId);
    }

    [Fact]
    public void Lock_released_when_source_disappears()
    {
        var (reg, clock) = Create();
        clock.Advance();
        reg.SetExtensionSources([Tab(1, true)]);
        reg.SetSmtcSources([Smtc("sp", true)]);
        reg.Lock("tab:1");

        reg.SetExtensionSources([]);
        clock.Advance(TimeSpan.FromSeconds(11));
        reg.SetSmtcSources([Smtc("sp", true)]);

        Assert.Null(reg.LockedId);
        Assert.Equal("smtc:sp", reg.Current?.Id);
    }

    [Fact]
    public void Lock_survives_quick_reconnect()
    {
        var (reg, clock) = Create();
        clock.Advance();
        reg.SetExtensionSources([Tab(1, true)]);
        reg.SetSmtcSources([Smtc("sp", true)]);
        reg.Lock("tab:1");

        reg.ClearExtensionSources();
        Assert.Equal("tab:1", reg.Current?.Id); // a short gap: the tab is kept for a few seconds
        clock.Advance(TimeSpan.FromSeconds(4));
        reg.SetSmtcSources([Smtc("sp", true)]);
        Assert.Equal("smtc:sp", reg.Current?.Id); // gone longer: normal rule applies
        clock.Advance(TimeSpan.FromSeconds(1));
        reg.SetExtensionSources([Tab(1, true)]);

        Assert.Equal("tab:1", reg.LockedId);
        Assert.Equal("tab:1", reg.Current?.Id);
    }

    [Fact]
    public void Lock_released_after_grace_period()
    {
        var (reg, clock) = Create();
        clock.Advance();
        reg.SetExtensionSources([Tab(1, true)]);
        reg.Lock("tab:1");

        reg.ClearExtensionSources();
        clock.Advance(TimeSpan.FromSeconds(11));
        reg.SetExtensionSources([Tab(1, true)]); // too late

        Assert.Null(reg.LockedId);
    }

    [Fact]
    public void Opera_duplicate_hidden_when_smtc_title_has_youtube_suffix()
    {
        var (reg, clock) = Create();
        clock.Advance();
        reg.SetExtensionSources([Tab(1, true, "Song")]);
        reg.SetSmtcSources([Smtc("opera", true, "Song - YouTube", "Opera")]);

        Assert.Equal("tab:1", reg.Current?.Id);
        Assert.Empty(reg.Others);
    }

    [Fact]
    public void ClearExtensionSources_removes_tabs_and_releases_lock()
    {
        var (reg, clock) = Create();
        clock.Advance();
        reg.SetExtensionSources([Tab(1, true)]);
        reg.SetSmtcSources([Smtc("sp", false)]);
        reg.Lock("tab:1");

        reg.ClearExtensionSources();
        clock.Advance(TimeSpan.FromSeconds(11));
        reg.ClearExtensionSources();

        Assert.Null(reg.LockedId);
        Assert.Equal("smtc:sp", reg.Current?.Id);
        Assert.Empty(reg.Others);
    }

    [Fact]
    public void Opera_smtc_hidden_when_title_matches_tab()
    {
        var (reg, clock) = Create();
        clock.Advance();
        reg.SetExtensionSources([Tab(1, true, "Song X")]);
        reg.SetSmtcSources([Smtc("opera", true, " song x ", "Opera")]);

        Assert.Equal("tab:1", reg.Current?.Id);
        Assert.Empty(reg.Others);
    }

    [Fact]
    public void Opera_smtc_shown_when_title_differs()
    {
        var (reg, clock) = Create();
        clock.Advance();
        reg.SetExtensionSources([Tab(1, true, "Song X")]);
        reg.SetSmtcSources([Smtc("opera", true, "Other", "Opera")]);

        Assert.Equal(["smtc:opera"], reg.Others.Select(o => o.Id));
    }

    [Fact]
    public void Two_tabs_same_title_both_visible()
    {
        var (reg, clock) = Create();
        clock.Advance();
        reg.SetExtensionSources([Tab(1, true, "Same"), Tab(2, true, "Same")]);

        var ids = new[] { reg.Current!.Id }.Concat(reg.Others.Select(o => o.Id)).Order().ToArray();
        Assert.Equal(["tab:1", "tab:2"], ids);
    }

    [Fact]
    public void Select_shows_paused_source_until_another_starts()
    {
        var (reg, clock) = Create();
        clock.Advance();
        reg.SetExtensionSources([Tab(1, true), Tab(2, false), Tab(3, false)]);
        clock.Advance();
        reg.Select("tab:2");
        Assert.Equal("tab:2", reg.Current?.Id);

        clock.Advance();
        reg.SetExtensionSources([Tab(1, true), Tab(2, false), Tab(3, true)]);
        Assert.Equal("tab:3", reg.Current?.Id);
    }

    [Fact]
    public void Changed_raised_on_lock()
    {
        var (reg, clock) = Create();
        clock.Advance();
        reg.SetExtensionSources([Tab(1, true)]);
        var count = 0;
        reg.Changed += (_, _) => count++;

        reg.Lock("tab:1");

        Assert.Equal(1, count);
    }

    [Fact]
    public void Changed_not_raised_when_nothing_changed()
    {
        var (reg, clock) = Create();
        clock.Advance();
        var sources = new[] { Tab(1, true) };
        reg.SetExtensionSources(sources);
        var count = 0;
        reg.Changed += (_, _) => count++;

        reg.SetExtensionSources(sources);
        reg.Lock("tab:99");   // not visible: no-op

        Assert.Equal(0, count);
    }
    [Fact]
    public void Tab_missing_briefly_is_kept_then_dropped()
    {
        var (reg, clock) = Create();
        clock.Advance();
        reg.SetExtensionSources([Tab(1, true)]);

        reg.SetExtensionSources([]); // e.g. a track change or the extension's worker restarting
        Assert.Equal("tab:1", reg.Current?.Id);

        clock.Advance(TimeSpan.FromSeconds(4));
        reg.SetExtensionSources([]);
        Assert.Null(reg.Current);
    }

    [Fact]
    public void Tab_gets_cover_bytes_from_its_Opera_twin()
    {
        var (reg, clock) = Create();
        clock.Advance();
        byte[] cover = [1, 2, 3];
        reg.SetExtensionSources([Tab(1, true, "Song")]);
        reg.SetSmtcSources([Smtc("opera", true, "Song", "Opera") with { CoverBytes = cover }]);

        Assert.Equal("tab:1", reg.Current?.Id);
        Assert.Same(cover, reg.Current?.CoverBytes);
        Assert.Empty(reg.Others);
    }
}