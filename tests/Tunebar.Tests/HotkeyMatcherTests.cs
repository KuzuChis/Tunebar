using Tunebar.Ui;

namespace Tunebar.Tests;

public class HotkeyMatcherTests
{
    private static readonly ModifierState All = new(true, false, true, false, true, false, false);

    [Theory]
    [InlineData(0x20u, HotkeyAction.PlayPause)]
    [InlineData(0x27u, HotkeyAction.Next)]
    [InlineData(0x25u, HotkeyAction.Prev)]
    [InlineData(0x26u, HotkeyAction.VolumeUp)]
    [InlineData(0x28u, HotkeyAction.VolumeDown)]
    public void All_five_combos_match(uint vk, HotkeyAction expected)
    {
        var m = HotkeyMatcher.Match(vk, false, All);

        Assert.NotNull(m);
        Assert.Equal(expected, m!.Value.Action);
        Assert.True(m.Value.Fire);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Missing_any_modifier_is_no_match(int missing)
    {
        var mods = new ModifierState(
            missing != 0, false, missing != 1, false, missing != 2, false, false);

        Assert.Null(HotkeyMatcher.Match(0x26, false, mods));
    }

    [Fact]
    public void Win_held_is_no_match()
    {
        Assert.Null(HotkeyMatcher.Match(0x26, false, All with { Win = true }));
    }

    [Fact]
    public void Unrelated_key_is_no_match()
    {
        Assert.Null(HotkeyMatcher.Match(0x41, false, All));
    }

    [Theory]
    [InlineData(0x20u)]
    [InlineData(0x25u)]
    [InlineData(0x27u)]
    public void Play_next_prev_do_not_repeat_but_are_still_swallowed(uint vk)
    {
        var m = HotkeyMatcher.Match(vk, true, All);

        Assert.NotNull(m);
        Assert.False(m!.Value.Fire);
    }

    [Theory]
    [InlineData(0x26u)]
    [InlineData(0x28u)]
    public void Volume_keys_repeat(uint vk)
    {
        Assert.True(HotkeyMatcher.Match(vk, true, All)!.Value.Fire);
    }

    [Fact]
    public void Right_shift_released_while_left_shift_held_still_matches()
    {
        var mods = new ModifierState(true, false, true, false, LShift: true, RShift: false, Win: false);

        Assert.NotNull(HotkeyMatcher.Match(0x26, true, mods));
    }

    [Fact]
    public void Right_side_modifiers_count_too()
    {
        var mods = new ModifierState(false, true, false, true, false, true, false);

        Assert.NotNull(HotkeyMatcher.Match(0x28, false, mods));
    }
}
