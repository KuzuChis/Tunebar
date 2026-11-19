using Tunebar.Windows;

namespace Tunebar.Tests;

public class CoverRefreshPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static CoverState State(byte[]? bytes, double readAgo = 0, double titleAgo = 0, bool dirty = false) => new()
    {
        Title = "A", Bytes = bytes, ReadAt = T0.AddSeconds(-readAgo), TitleSince = T0.AddSeconds(-titleAgo), Dirty = dirty,
    };

    [Fact]
    public void No_state_reads() => Assert.True(CoverRefreshPolicy.ShouldRead(null, "A", T0));

    [Fact]
    public void New_title_reads() => Assert.True(CoverRefreshPolicy.ShouldRead(State([1], 5, 5), "B", T0));

    [Fact]
    public void Dirty_reads() => Assert.True(CoverRefreshPolicy.ShouldRead(State([1], 0, 5, dirty: true), "A", T0));

    [Fact]
    public void Same_title_with_cover_is_reused() => Assert.False(CoverRefreshPolicy.ShouldRead(State([1], 10, 10), "A", T0));

    [Fact]
    public void Missing_cover_retries_after_one_second() =>
        Assert.True(CoverRefreshPolicy.ShouldRead(State(null, 1, 5), "A", T0));

    [Fact]
    public void Missing_cover_does_not_retry_within_one_second() =>
        Assert.False(CoverRefreshPolicy.ShouldRead(State(null, 0.5, 5), "A", T0));

    [Fact]
    public void Missing_cover_stops_retrying_after_thirty_seconds() =>
        Assert.False(CoverRefreshPolicy.ShouldRead(State(null, 2, 30), "A", T0));
}
