using MediaTray.Core;

namespace MediaTray.Tests;

public class LoopCycleTests
{
    [Theory]
    [InlineData(LoopMode.Off, LoopMode.Playlist)]
    [InlineData(LoopMode.Playlist, LoopMode.One)]
    [InlineData(LoopMode.One, LoopMode.Off)]
    public void Next_cycles_all_modes(LoopMode cur, LoopMode exp) =>
        Assert.Equal(exp, LoopCycle.Next(cur, [LoopMode.Off, LoopMode.Playlist, LoopMode.One]));

    [Fact]
    public void Next_skips_unsupported() =>
        Assert.Equal(LoopMode.One, LoopCycle.Next(LoopMode.Off, [LoopMode.Off, LoopMode.One]));

    [Fact]
    public void Next_with_empty_support_returns_current() =>
        Assert.Equal(LoopMode.Off, LoopCycle.Next(LoopMode.Off, []));
}
